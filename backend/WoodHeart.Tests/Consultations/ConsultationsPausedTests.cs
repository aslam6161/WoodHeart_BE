using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Consultations;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// The shop stopping consultations for a while.
/// </summary>
/// <remarks>
/// <para>
/// The shop has one consultant. Travel, illness, or a diary already full for a
/// month are all reasons to stop taking bookings without deleting the services
/// and the rules behind them.
/// </para>
/// <para>
/// <b>The refusal is a conflict, not a not-found.</b> The services still exist
/// and the shop means to offer them again; telling somebody their consultation
/// does not exist when it is merely paused sends them to a competitor instead
/// of back next week.
/// </para>
/// <para>
/// <b>Existing bookings are untouched.</b> Turning off new work must not lock
/// the shop out of the work it already has — somebody has an afternoon blocked
/// out and a customer expecting them.
/// </para>
/// </remarks>
public class ConsultationsPausedTests
{
    private readonly IConsultationServiceRepository _services =
        Substitute.For<IConsultationServiceRepository>();

    private readonly IConsultantRepository _consultants = Substitute.For<IConsultantRepository>();
    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IFeatureFlagService _features = Substitute.For<IFeatureFlagService>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 25, 6, 0, 0, TimeSpan.Zero));

    private readonly AvailabilityService _availability;

    public ConsultationsPausedTests()
    {
        _currentUser.Language.Returns("en");

        _services.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([Service()]);
        _consultants.GetActiveAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns([]);
        _services.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Service());

        _availability = new AvailabilityService(
            _services, _consultants, _bookings, _currentUser, _features, _clock);
    }

    private void Paused() =>
        _features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, Arg.Any<CancellationToken>())
            .Returns(false);

    private void Open() =>
        _features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, Arg.Any<CancellationToken>())
            .Returns(true);

    // -------------------------------------------------------------------------
    // Paused
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Nothing_is_offered_while_the_shop_is_paused()
    {
        Paused();

        var result = await _availability.GetServicesAsync();

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ConsultationErrors.NotOffered);
    }

    [Fact]
    public async Task A_direct_link_to_one_is_refused_the_same_way()
    {
        // Somebody with yesterday's link, or a tab left open. They get the same
        // sentence as everyone else rather than a page that half works.
        Paused();

        var result = await _availability.GetServiceBySlugAsync("interior-consultation");

        result.ErrorCode.ShouldBe(ConsultationErrors.NotOffered);
    }

    [Fact]
    public async Task And_so_is_the_calendar()
    {
        Paused();

        var result = await _availability.GetAvailabilityAsync(
            new AvailabilityQueryDto
            {
                ServiceId = 1,
                From = new DateOnly(2026, 9, 26),
                To = new DateOnly(2026, 9, 27)
            });

        result.ErrorCode.ShouldBe(ConsultationErrors.NotOffered);
    }

    [Fact]
    public async Task And_the_list_of_consultants()
    {
        Paused();

        var result = await _availability.GetConsultantsAsync();

        result.ErrorCode.ShouldBe(ConsultationErrors.NotOffered);
    }

    [Fact]
    public void The_refusal_is_a_conflict_rather_than_a_not_found()
    {
        // The suffix is what BaseApiController maps to a status code, so this
        // is the assertion that decides what the customer's browser is told.
        ConsultationErrors.NotOffered.ShouldEndWith(".conflict");
        ConsultationErrors.NotOffered.ShouldNotContain("not_found");
    }

    [Fact]
    public async Task Nothing_is_read_from_the_database_while_it_is_paused()
    {
        // The point of checking first: a paused shop should not be running
        // availability arithmetic over every consultant's diary to then throw
        // the answer away.
        Paused();

        await _availability.GetServicesAsync();

        await _services.DidNotReceive().GetActiveAsync(Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Open again
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Turning_it_back_on_needs_nothing_else_restored()
    {
        // The services and the rules behind them were never deleted, which is
        // the whole reason the switch exists rather than the shop deactivating
        // every service one by one.
        Open();

        var result = await _availability.GetServicesAsync();

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_flag_leaves_the_shop_shut_rather_than_open()
    {
        // FeatureFlagService answers false for a flag it cannot find. Worth
        // pinning: the failure a missing row causes should be a shop that
        // politely declines, not one taking bookings nobody will keep.
        _features.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _availability.GetServicesAsync();

        result.ErrorCode.ShouldBe(ConsultationErrors.NotOffered);
    }

    private static ConsultationService Service() =>
        new()
        {
            Id = 1,
            Slug = Slug.From("interior-consultation"),
            Name = LocalizedText.Create("Interior consultation"),
            IsActive = true,
            DurationMinutes = 60,
            Fee = Money.Taka(2_000m)
        };
}
