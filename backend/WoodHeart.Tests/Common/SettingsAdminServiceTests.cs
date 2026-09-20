using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.DTOs.Common;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Common;

namespace WoodHeart.Tests.Common;

/// <summary>
/// The settings screen's rules, and what a save does and does not do.
/// </summary>
/// <remarks>
/// The rules matter because the values are applied to the next order placed.
/// A VAT rate of 750 is a slipped decimal point; "7,5" on a European-locale
/// machine is 75 to the reader; a negative delivery charge is a discount
/// nobody meant. Each of those is one line here, and each would otherwise be
/// found by a customer.
/// </remarks>
public class SettingsAdminServiceTests
{
    private readonly IStoreSettingRepository _repository = Substitute.For<IStoreSettingRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IStoreSettingService _cache = Substitute.For<IStoreSettingService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private readonly List<StoreSetting> _table =
    [
        Setting(SettingKeys.VatRate, "7.5", SettingValueType.Decimal, "Tax"),
        Setting(SettingKeys.PricesIncludeVat, "true", SettingValueType.Boolean, "Tax"),
        Setting(SettingKeys.DeliveryChargeInsideDhaka, "0", SettingValueType.Decimal, "Delivery"),
        Setting(SettingKeys.LowStockThreshold, "5", SettingValueType.Integer, "Inventory"),
        Setting(SettingKeys.OrderNumberPrefix, "WH", SettingValueType.String, "Orders"),
        Setting(SettingKeys.StoreName, "WoodHeart", SettingValueType.String, "Store"),
        Setting(SettingKeys.StorePhone, "", SettingValueType.String, "Store"),
        Setting(SettingKeys.StoreBin, "", SettingValueType.String, "Store")
    ];

    public SettingsAdminServiceTests()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _table.ToList());
        _currentUser.PhoneNumber.Returns("+8801712345678");
    }

    private SettingsAdminService CreateService() =>
        new(_repository, _unitOfWork, _cache, _currentUser, NullLogger<SettingsAdminService>.Instance);

    // --- The rules, one line each ------------------------------------------

    [Theory]
    [InlineData(SettingKeys.VatRate, "15", "15")]
    [InlineData(SettingKeys.VatRate, " 7.50 ", "7.50")]
    [InlineData(SettingKeys.VatRate, "0", "0")]
    [InlineData(SettingKeys.PricesIncludeVat, "True", "true")]
    [InlineData(SettingKeys.PricesIncludeVat, "false", "false")]
    [InlineData(SettingKeys.LowStockThreshold, "12", "12")]
    [InlineData(SettingKeys.OrderNumberPrefix, "wh", "WH")]
    [InlineData(SettingKeys.StorePhone, "01712-345678", "+8801712345678")]
    [InlineData(SettingKeys.StorePhone, "", "")]
    [InlineData(SettingKeys.StoreBin, "001234567-0101", "001234567-0101")]
    public void A_value_is_stored_in_its_normalised_spelling(string key, string typed, string stored)
    {
        var (value, error) = SettingsAdminService.Normalise(Find(key), typed);

        error.ShouldBeNull();
        value.ShouldBe(stored);
    }

    [Theory]
    [InlineData(SettingKeys.VatRate, "750")]
    [InlineData(SettingKeys.VatRate, "-1")]
    [InlineData(SettingKeys.VatRate, "seven")]
    [InlineData(SettingKeys.VatRate, "")]
    [InlineData(SettingKeys.DeliveryChargeInsideDhaka, "-500")]
    [InlineData(SettingKeys.PricesIncludeVat, "yes")]
    [InlineData(SettingKeys.LowStockThreshold, "2.5")]
    [InlineData(SettingKeys.LowStockThreshold, "-1")]
    [InlineData(SettingKeys.OrderNumberPrefix, "")]
    [InlineData(SettingKeys.OrderNumberPrefix, "WH-")]
    [InlineData(SettingKeys.OrderNumberPrefix, "WOODHEART")]
    [InlineData(SettingKeys.StoreName, "")]
    [InlineData(SettingKeys.StorePhone, "12345")]
    [InlineData(SettingKeys.StoreBin, "BIN 123")]
    public void A_value_that_breaks_its_rule_is_refused(string key, string typed)
    {
        var (value, error) = SettingsAdminService.Normalise(Find(key), typed);

        value.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_European_decimal_comma_is_not_quietly_ten_times_the_rate()
    {
        // "7,5" parsed with NumberStyles.Number and the invariant culture is
        // 75 — the comma is a thousands separator. The reader would then
        // charge 75% VAT. Refusing it is the least surprising thing to do;
        // storing 75 is the most.
        var (value, error) = SettingsAdminService.Normalise(Find(SettingKeys.VatRate), "7,5");

        value.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    // --- The save ----------------------------------------------------------

    [Fact]
    public async Task A_save_with_one_bad_value_writes_nothing()
    {
        var result = await CreateService().UpdateAsync(new UpdateSettingsDto
        {
            Values = new Dictionary<string, string?>
            {
                [SettingKeys.VatRate] = "15",
                [SettingKeys.DeliveryChargeInsideDhaka] = "-100"
            }
        });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(SettingsErrors.InvalidValue);
        result.Errors.ShouldNotBeNull();
        result.Errors.Keys.ShouldBe([SettingKeys.DeliveryChargeInsideDhaka]);

        // The good value did not land either. VAT and delivery are one
        // decision on one screen; half of it must not be applied.
        Find(SettingKeys.VatRate).Value.ShouldBe("7.5");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _cache.DidNotReceive().Invalidate(Arg.Any<string>());
    }

    [Fact]
    public async Task A_good_save_writes_then_invalidates_each_changed_key()
    {
        var result = await CreateService().UpdateAsync(new UpdateSettingsDto
        {
            Values = new Dictionary<string, string?>
            {
                [SettingKeys.VatRate] = "15",
                [SettingKeys.StoreName] = "WoodHeart Interiors",
                // Unchanged: sent back by the screen as it was.
                [SettingKeys.OrderNumberPrefix] = "WH"
            }
        });

        result.IsSuccess.ShouldBeTrue();
        Find(SettingKeys.VatRate).Value.ShouldBe("15");
        Find(SettingKeys.StoreName).Value.ShouldBe("WoodHeart Interiors");

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _cache.Invalidate(SettingKeys.VatRate);
            _cache.Invalidate(SettingKeys.StoreName);
        });

        // Not for the one that did not change: its cache entry is still right.
        _cache.DidNotReceive().Invalidate(SettingKeys.OrderNumberPrefix);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_does_not_touch_the_database()
    {
        var result = await CreateService().UpdateAsync(new UpdateSettingsDto
        {
            Values = new Dictionary<string, string?> { [SettingKeys.VatRate] = "7.5" }
        });

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_key_is_refused_rather_than_created()
    {
        var result = await CreateService().UpdateAsync(new UpdateSettingsDto
        {
            Values = new Dictionary<string, string?> { ["tax.vat_rat"] = "15" }
        });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(SettingsErrors.UnknownKey);
        await _repository.DidNotReceive().InsertAsync(Arg.Any<StoreSetting>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_list_comes_back_in_the_order_the_screen_shows_it()
    {
        var result = await CreateService().GetAllAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Data.ShouldNotBeNull();

        // Store first — it is the part a new owner fills in first — then the
        // money, then the housekeeping.
        result.Data.Select(x => x.Category).Distinct()
            .ShouldBe(["Store", "Tax", "Delivery", "Orders", "Inventory"]);
    }

    private StoreSetting Find(string key) => _table.Single(x => x.Key == key);

    private static StoreSetting Setting(string key, string value, SettingValueType type, string category) =>
        new() { Key = key, Value = value, ValueType = type, Category = category, IsSystem = true };
}
