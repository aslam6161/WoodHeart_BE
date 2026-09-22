using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Promotions;
using WoodHeart.Service.DTOs.Promotions;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Promotions;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Promotions;

/// <summary>
/// Writing discounts: what is refused, and what a discount that has already
/// been given still allows.
/// </summary>
public class DiscountAdminServiceTests
{
    private readonly IDiscountRepository _discounts = Substitute.For<IDiscountRepository>();
    private readonly IPromotionUsageRepository _usages = Substitute.For<IPromotionUsageRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();

    private readonly DiscountAdminService _service;

    public DiscountAdminServiceTests()
    {
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<DiscountDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<DiscountDto>>>>()(CancellationToken.None));

        _service = new DiscountAdminService(
            _discounts, _usages, _categories, _products, _currentUser, _clock, _unitOfWork,
            NullLogger<DiscountAdminService>.Instance);
    }

    [Fact]
    public async Task A_code_another_discount_already_holds_is_refused()
    {
        _discounts.CodeExistsAsync("EID25", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.CreateAsync(Save(code: "eid25"));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PromotionErrors.CodeTaken);
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 0)]
    [InlineData(DiscountType.Percentage, 101)]
    [InlineData(DiscountType.FixedAmount, 0)]
    public async Task A_value_that_cannot_mean_anything_is_refused(DiscountType type, decimal value)
    {
        var result = await _service.CreateAsync(Save(type: type, value: value));

        result.ErrorCode.ShouldBe(PromotionErrors.ValueInvalid);
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        var result = await _service.CreateAsync(
            Save(startsAt: _clock.UtcNow.AddDays(3), endsAt: _clock.UtcNow.AddDays(1)));

        result.ErrorCode.ShouldBe(PromotionErrors.WindowInvalid);
    }

    [Fact]
    public async Task A_restriction_naming_both_a_product_and_a_category_is_refused()
    {
        // Neither would match nothing and both would match twice; both are
        // silent, which is why the database carries the same rule.
        var result = await _service.CreateAsync(
            Save(targets: [new DiscountTargetDto { CategoryId = 1, ProductId = 2 }]));

        result.ErrorCode.ShouldBe(PromotionErrors.TargetInvalid);
    }

    [Fact]
    public async Task A_restriction_pointing_at_nothing_is_refused()
    {
        _categories.AnyAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<Domain.Entity.Catalog.Category, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _service.CreateAsync(
            Save(targets: [new DiscountTargetDto { CategoryId = 99 }]));

        result.ErrorCode.ShouldBe(PromotionErrors.TargetNotFound);
    }

    [Fact]
    public async Task A_code_is_stored_upper_cased_whatever_was_typed()
    {
        Discount? written = null;

        await _discounts.InsertAsync(
            Arg.Do<Discount>(discount => written = discount), Arg.Any<CancellationToken>());

        var result = await _service.CreateAsync(Save(code: " eid25 "));

        result.IsSuccess.ShouldBeTrue();
        written.ShouldNotBeNull();

        // The admin writing a code, the customer typing one and the unique
        // index all have to agree on what "the same code" means.
        written.Code.ShouldBe("EID25");
    }

    [Fact]
    public async Task Free_shipping_stores_no_value_whatever_was_sent()
    {
        Discount? written = null;

        await _discounts.InsertAsync(
            Arg.Do<Discount>(discount => written = discount), Arg.Any<CancellationToken>());

        await _service.CreateAsync(Save(type: DiscountType.FreeShipping, value: 42m));

        written!.Value.ShouldBe(0m);
    }

    [Fact]
    public async Task A_discount_that_has_been_given_cannot_have_its_value_changed()
    {
        var existing = Existing();

        _discounts.GetWithTargetsAsync(1, Arg.Any<CancellationToken>()).Returns(existing);
        _usages.TotalsAsync(1, Arg.Any<CancellationToken>()).Returns((3, 6_000m));

        var result = await _service.UpdateAsync(1, Save(value: 25m));

        result.ErrorCode.ShouldBe(PromotionErrors.DiscountInUse);

        // And the row is untouched, rather than half-written.
        existing.Value.ShouldBe(10m);
    }

    [Fact]
    public async Task A_discount_that_has_been_given_can_still_be_renamed_and_paused()
    {
        // The parts an order that already used it has no claim on. Stopping a
        // campaign must never require deleting the record of what it gave.
        var existing = Existing();

        _discounts.GetWithTargetsAsync(1, Arg.Any<CancellationToken>()).Returns(existing);
        _usages.TotalsAsync(1, Arg.Any<CancellationToken>()).Returns((3, 6_000m));

        var result = await _service.UpdateAsync(
            1, Save(name: "September sale (closed)", status: DiscountStatus.Paused));

        result.IsSuccess.ShouldBeTrue();
        existing.Name.ShouldBe("September sale (closed)");
        existing.Status.ShouldBe(DiscountStatus.Paused);
    }

    [Fact]
    public async Task Archiving_is_a_status_change_rather_than_a_delete()
    {
        var existing = Existing();

        _discounts.GetWithTargetsAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _service.SetStatusAsync(1, DiscountStatus.Archived);

        result.IsSuccess.ShouldBeTrue();
        existing.Status.ShouldBe(DiscountStatus.Archived);
        _discounts.DidNotReceiveWithAnyArgs().Delete(default!);
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static Discount Existing() =>
        new()
        {
            Id = 1,
            Name = "September sale",
            Type = DiscountType.Percentage,
            Value = 10m,
            Status = DiscountStatus.Active
        };

    private static SaveDiscountDto Save(
        string name = "September sale",
        string? code = null,
        DiscountType type = DiscountType.Percentage,
        decimal value = 10m,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        DiscountStatus status = DiscountStatus.Active,
        IReadOnlyList<DiscountTargetDto>? targets = null) =>
        new()
        {
            Name = name,
            Code = code,
            Type = type,
            Value = value,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = status,
            Targets = targets ?? []
        };
}
