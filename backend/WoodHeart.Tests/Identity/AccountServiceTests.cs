using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Identity;
using WoodHeart.Service.DTOs.Identity;
using WoodHeart.Service.Infrastructure.Security;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Identity;
using WoodHeart.Service.Services.Identity;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Identity;

/// <summary>
/// Sign-in, registration, and the rotation rules that carry a session.
/// </summary>
/// <remarks>
/// <para>
/// A real <see cref="TokenHasher"/> is used rather than a substitute. The point
/// of several tests below is that the plaintext refresh token never reaches the
/// database, and a substituted hasher returning the input would let exactly
/// that bug pass.
/// </para>
/// <para>
/// <see cref="UserManager{TUser}"/> is substituted, which is ugly — it needs
/// nine constructor arguments — but the alternative is a real Identity stack
/// over a real database for tests that are about our rules, not Identity's.
/// The one thing that genuinely needs a database, the unique index on the
/// token hash, is not asserted here.
/// </para>
/// </remarks>
public class AccountServiceTests
{
    private readonly UserManager<AppUser> _users = MockUserManager();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly TokenHasher _hasher = new();
    private readonly IUserRefreshTokenRepository _refreshTokens =
        Substitute.For<IUserRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private const string Phone = "01712345678";
    private const string E164 = "+8801712345678";
    private const string Password = "correct-horse-battery";

    /// <summary>Rows handed to <c>InsertAsync</c>, so tests can inspect what was stored.</summary>
    private readonly List<UserRefreshToken> _inserted = [];

    public AccountServiceTests()
    {
        _tokens.CreateAccessToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>())
            .Returns(new AccessToken("access-token", _clock.UtcNow.AddMinutes(15)));

        _tokens.CreateRefreshToken().Returns(_ => Guid.NewGuid().ToString("n"));

        _currentUser.IpAddress.Returns("203.0.113.9");

        _refreshTokens
            .When(repository => repository.InsertAsync(
                Arg.Any<UserRefreshToken>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var row = call.Arg<UserRefreshToken>();

                // The database would assign this. Several tests need it, because
                // rotation points the old token at the new one by id.
                row.Id = _inserted.Count + 1;
                _inserted.Add(row);
            });

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<AuthenticatedUserDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<AuthenticatedUserDto>>>>()(
                    CancellationToken.None));
    }

    private AccountService CreateService() =>
        new(_users,
            _tokens,
            _hasher,
            _refreshTokens,
            _unitOfWork,
            _clock,
            _currentUser,
            Options.Create(new JwtSettings { RefreshTokenDays = 30 }),
            NullLogger<AccountService>.Instance);

    private static UserManager<AppUser> MockUserManager() =>
        Substitute.For<UserManager<AppUser>>(
            Substitute.For<IUserStore<AppUser>>(),
            null, null, null, null, null, null, null, null);

    private AppUser GivenAnAccount(bool isActive = true, bool lockedOut = false)
    {
        var user = new AppUser
        {
            Id = 42,
            UserName = E164,
            PhoneNumber = E164,
            FullName = "Ayesha Rahman",
            IsActive = isActive
        };

        _users.FindByNameAsync(E164).Returns(user);
        _users.FindByIdAsync("42").Returns(user);
        _users.IsLockedOutAsync(user).Returns(lockedOut);
        _users.CheckPasswordAsync(user, Password).Returns(true);
        _users.CheckPasswordAsync(user, Arg.Is<string>(p => p != Password)).Returns(false);
        _users.GetRolesAsync(user).Returns([Roles.Admin]);

        return user;
    }

    // --- Sign in -------------------------------------------------------------

    [Fact]
    public async Task Accepts_the_number_in_any_format_a_customer_types()
    {
        GivenAnAccount();

        foreach (var typed in new[] { "01712345678", "+8801712345678", "8801712345678", "017-1234-5678" })
        {
            var result = await CreateService().LoginAsync(
                new LoginDto { PhoneNumber = typed, Password = Password });

            // All four are the same account. A login form that disagrees is a
            // support call, and the customer is not wrong.
            result.IsSuccess.ShouldBeTrue($"'{typed}' should sign in: {result.Message}");
        }
    }

    [Fact]
    public async Task Says_exactly_the_same_thing_for_an_unknown_number_and_a_wrong_password()
    {
        GivenAnAccount();

        var wrongPassword = await CreateService().LoginAsync(
            new LoginDto { PhoneNumber = Phone, Password = "not-the-password" });

        _users.FindByNameAsync(E164).Returns((AppUser?)null);

        var noSuchAccount = await CreateService().LoginAsync(
            new LoginDto { PhoneNumber = Phone, Password = Password });

        // The whole point. Any difference here — code, message, even a
        // measurable difference in timing — turns the login form into a "is
        // this number one of your customers?" lookup for anybody who asks.
        noSuchAccount.ErrorCode.ShouldBe(wrongPassword.ErrorCode);
        noSuchAccount.Message.ShouldBe(wrongPassword.Message);
        noSuchAccount.ErrorCode.ShouldBe(IdentityErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Counts_a_wrong_password_towards_the_lockout()
    {
        var user = GivenAnAccount();

        await CreateService().LoginAsync(new LoginDto { PhoneNumber = Phone, Password = "wrong" });

        // Without this call MaxFailedAccessAttempts is a configured policy that
        // never fires — the settings look right and brute force is unlimited.
        await _users.Received(1).AccessFailedAsync(user);
    }

    [Fact]
    public async Task Clears_the_failure_count_after_a_good_password()
    {
        var user = GivenAnAccount();

        await CreateService().LoginAsync(new LoginDto { PhoneNumber = Phone, Password = Password });

        // Otherwise four typos spread over a month eventually lock out someone
        // who has been signing in successfully the whole time.
        await _users.Received(1).ResetAccessFailedCountAsync(user);
    }

    [Fact]
    public async Task Refuses_a_locked_account_without_checking_the_password()
    {
        var user = GivenAnAccount(lockedOut: true);

        var result = await CreateService().LoginAsync(
            new LoginDto { PhoneNumber = Phone, Password = Password });

        result.ErrorCode.ShouldBe(IdentityErrors.AccountLocked);

        // Checking anyway would let an attacker confirm a password during a
        // lockout, which is most of what the lockout is protecting.
        await _users.DidNotReceive().CheckPasswordAsync(user, Arg.Any<string>());
    }

    [Fact]
    public async Task Refuses_a_disabled_account()
    {
        GivenAnAccount(isActive: false);

        var result = await CreateService().LoginAsync(
            new LoginDto { PhoneNumber = Phone, Password = Password });

        // 403, not 401: the client must not try to refresh its way out of this.
        result.ErrorCode.ShouldBe(IdentityErrors.AccountDisabled);
        result.ErrorCode.ShouldEndWith(".forbidden");
    }

    [Fact]
    public async Task Rejects_a_number_that_is_not_a_Bangladeshi_mobile()
    {
        var result = await CreateService().LoginAsync(
            new LoginDto { PhoneNumber = "0121234567", Password = Password });

        result.IsSuccess.ShouldBeFalse();
        await _users.DidNotReceive().FindByNameAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Stores_only_a_hash_of_the_refresh_token()
    {
        GivenAnAccount();

        var result = await CreateService().LoginAsync(
            new LoginDto { PhoneNumber = Phone, Password = Password });

        var issued = result.Data!.RefreshToken.ShouldNotBeNull();
        var stored = _inserted.ShouldHaveSingleItem();

        // A database dump must not be a set of live sessions.
        stored.TokenHash.ShouldNotBe(issued);
        stored.TokenHash.ShouldBe(_hasher.Hash(issued));
        stored.ExpiresAt.ShouldBe(_clock.UtcNow.AddDays(30));
        stored.CreatedByIp.ShouldBe("203.0.113.9");
    }

    [Fact]
    public async Task Records_when_the_account_last_signed_in()
    {
        var user = GivenAnAccount();

        await CreateService().LoginAsync(new LoginDto { PhoneNumber = Phone, Password = Password });

        user.LastLoginAt.ShouldBe(_clock.UtcNow);
    }

    // --- Register ------------------------------------------------------------

    [Fact]
    public async Task Registration_normalises_the_number_and_grants_only_the_Customer_role()
    {
        _users.FindByNameAsync(E164).Returns((AppUser?)null);
        _users.CreateAsync(Arg.Any<AppUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _users.GetRolesAsync(Arg.Any<AppUser>()).Returns([Roles.Customer]);

        var result = await CreateService().RegisterAsync(new RegisterDto
        {
            FullName = "  Ayesha Rahman  ",
            PhoneNumber = "017-1234-5678",
            Password = Password
        });

        result.IsSuccess.ShouldBeTrue(result.Message);

        await _users.Received(1).CreateAsync(
            Arg.Is<AppUser>(u => u.UserName == E164 && u.FullName == "Ayesha Rahman"),
            Password);

        // A public endpoint that can hand out anything but Customer is the
        // whole game. Staff roles are granted from the admin panel.
        await _users.Received(1).AddToRoleAsync(Arg.Any<AppUser>(), Roles.Customer);
        await _users.DidNotReceive().AddToRoleAsync(Arg.Any<AppUser>(), Roles.Admin);
        await _users.DidNotReceive().AddToRoleAsync(Arg.Any<AppUser>(), Roles.Manager);
        await _users.DidNotReceive().AddToRoleAsync(Arg.Any<AppUser>(), Roles.Staff);
    }

    [Fact]
    public async Task Registration_reports_a_taken_number_against_the_field()
    {
        GivenAnAccount();

        var result = await CreateService().RegisterAsync(new RegisterDto
        {
            FullName = "Someone Else",
            PhoneNumber = Phone,
            Password = Password
        });

        result.ErrorCode.ShouldBe(IdentityErrors.PhoneTaken);

        // Under the control, not in a banner at the top of a form the customer
        // has already scrolled past.
        result.Errors.ShouldNotBeNull().ShouldContainKey("phoneNumber");
        await _users.DidNotReceive().CreateAsync(Arg.Any<AppUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Registration_keeps_an_invalid_email_out_of_the_database()
    {
        _users.FindByNameAsync(E164).Returns((AppUser?)null);

        var result = await CreateService().RegisterAsync(new RegisterDto
        {
            FullName = "Ayesha Rahman",
            PhoneNumber = Phone,
            Email = "not-an-email",
            Password = Password
        });

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldNotBeNull().ShouldContainKey("email");
    }

    // --- Refresh -------------------------------------------------------------

    private UserRefreshToken GivenALiveRefreshToken(string plaintext)
    {
        var row = new UserRefreshToken
        {
            Id = 900,
            UserId = 42,
            TokenHash = _hasher.Hash(plaintext),
            CreatedAt = _clock.UtcNow.AddDays(-1),
            ExpiresAt = _clock.UtcNow.AddDays(29)
        };

        _refreshTokens.GetByHashAsync(row.TokenHash, Arg.Any<CancellationToken>()).Returns(row);

        return row;
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_points_the_old_one_at_the_new()
    {
        GivenAnAccount();
        var old = GivenALiveRefreshToken("the-live-token");

        var result = await CreateService().RefreshAsync("the-live-token");

        result.IsSuccess.ShouldBeTrue(result.Message);

        var replacement = _inserted.ShouldHaveSingleItem();

        old.RevokedAt.ShouldBe(_clock.UtcNow);
        old.ReplacedByTokenId.ShouldBe(replacement.Id);

        // The client is given the new one, not the one it presented.
        result.Data!.RefreshToken.ShouldNotBe("the-live-token");
        replacement.TokenHash.ShouldBe(_hasher.Hash(result.Data.RefreshToken!));
    }

    [Fact]
    public async Task A_token_presented_twice_kills_the_whole_chain()
    {
        GivenAnAccount();

        var reused = GivenALiveRefreshToken("already-used");
        reused.RevokedAt = _clock.UtcNow.AddMinutes(-5);

        var result = await CreateService().RefreshAsync("already-used");

        result.ErrorCode.ShouldBe(IdentityErrors.InvalidRefreshToken);

        // There is no way to tell the thief from the victim, so both are signed
        // out. Annoying once for the customer; a dead end for the attacker.
        await _refreshTokens.Received(1).RevokeChainAsync(
            reused.Id, _clock.UtcNow, "203.0.113.9", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_refuses_an_expired_token_without_issuing_anything()
    {
        GivenAnAccount();

        var expired = GivenALiveRefreshToken("stale");
        expired.ExpiresAt = _clock.UtcNow.AddSeconds(-1);

        var result = await CreateService().RefreshAsync("stale");

        result.ErrorCode.ShouldBe(IdentityErrors.InvalidRefreshToken);
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_refuses_an_unknown_or_missing_token()
    {
        _refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UserRefreshToken?)null);

        (await CreateService().RefreshAsync("never-issued")).ErrorCode
            .ShouldBe(IdentityErrors.InvalidRefreshToken);

        (await CreateService().RefreshAsync(null)).ErrorCode
            .ShouldBe(IdentityErrors.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_stops_working_the_moment_an_account_is_disabled()
    {
        var user = GivenAnAccount();
        user.IsActive = false;

        GivenALiveRefreshToken("the-live-token");

        var result = await CreateService().RefreshAsync("the-live-token");

        // The database is consulted on every refresh precisely so switching an
        // account off takes effect within the access token's fifteen minutes,
        // not at the end of the refresh token's thirty days.
        result.ErrorCode.ShouldBe(IdentityErrors.AccountDisabled);
        _inserted.ShouldBeEmpty();
    }

    // --- Sign out and password change ---------------------------------------

    [Fact]
    public async Task Signing_out_succeeds_even_for_a_token_the_server_has_never_seen()
    {
        _refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UserRefreshToken?)null);

        // The client is clearing its cookie either way. An error on the one
        // action meant to be reassuring helps nobody.
        (await CreateService().LogoutAsync("anything")).IsSuccess.ShouldBeTrue();
        (await CreateService().LogoutAsync(null)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_it_was_given()
    {
        var row = GivenALiveRefreshToken("the-live-token");

        await CreateService().LogoutAsync("the-live-token");

        row.RevokedAt.ShouldBe(_clock.UtcNow);
        row.RevokedByIp.ShouldBe("203.0.113.9");
    }

    [Fact]
    public async Task Changing_a_password_ends_every_other_session()
    {
        var user = GivenAnAccount();
        _currentUser.UserId.Returns(42L);
        _users.ChangePasswordAsync(user, "old", "a-brand-new-password")
            .Returns(IdentityResult.Success);

        var otherDevices = new List<UserRefreshToken>
        {
            new() { Id = 1, UserId = 42, TokenHash = "a", ExpiresAt = _clock.UtcNow.AddDays(10) },
            new() { Id = 2, UserId = 42, TokenHash = "b", ExpiresAt = _clock.UtcNow.AddDays(20) }
        };

        _refreshTokens.GetActiveForUserAsync(42, _clock.UtcNow, Arg.Any<CancellationToken>())
            .Returns(otherDevices);

        var result = await CreateService().ChangePasswordAsync(
            new ChangePasswordDto { CurrentPassword = "old", NewPassword = "a-brand-new-password" });

        result.IsSuccess.ShouldBeTrue(result.Message);

        // This is the entire reason someone changes a password they believe has
        // leaked. Leaving the attacker signed in makes the action theatre.
        otherDevices.ShouldAllBe(token => token.RevokedAt == _clock.UtcNow);
    }

    [Fact]
    public async Task A_failed_password_change_leaves_every_session_alone()
    {
        var user = GivenAnAccount();
        _currentUser.UserId.Returns(42L);
        _users.ChangePasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordMismatch",
                Description = "Incorrect password."
            }));

        var result = await CreateService().ChangePasswordAsync(
            new ChangePasswordDto { CurrentPassword = "wrong", NewPassword = "a-brand-new-password" });

        result.ErrorCode.ShouldBe(IdentityErrors.PasswordChangeFailed);
        result.Errors.ShouldNotBeNull().ShouldContainKey("password");

        await _refreshTokens.DidNotReceive().GetActiveForUserAsync(
            Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Me_reads_roles_from_the_database_rather_than_the_token()
    {
        var user = GivenAnAccount();
        _currentUser.UserId.Returns(42L);
        _users.GetRolesAsync(user).Returns([Roles.Manager]);

        var result = await CreateService().GetCurrentAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Data!.Roles.ShouldBe([Roles.Manager]);

        // And it must not quietly mint a token, which would make this endpoint
        // a second way to extend a session that only /refresh should extend.
        result.Data.AccessToken.ShouldBeEmpty();
        _tokens.DidNotReceive().CreateAccessToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task Me_refuses_an_account_that_has_since_been_disabled()
    {
        var user = GivenAnAccount();
        user.IsActive = false;
        _currentUser.UserId.Returns(42L);

        (await CreateService().GetCurrentAsync()).ErrorCode.ShouldBe(IdentityErrors.UserNotFound);
    }
}
