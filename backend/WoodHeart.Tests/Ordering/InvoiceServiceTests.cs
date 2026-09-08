using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Ordering;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// Producing the invoice for an order.
/// </summary>
/// <remarks>
/// The service's own job is small: find the order, gather the shop's details,
/// and hand both to the renderer. What is worth testing is what it does when
/// those details are <b>missing</b> — which is the state the shop is in right
/// now, with <c>store.name</c>, <c>store.address</c> and <c>store.bin</c> all
/// unset. An invoice that cannot be printed until someone fills in a settings
/// screen is an invoice that is not there when the van is waiting.
/// </remarks>
public class InvoiceServiceTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();

    private readonly IPaymentMethodConfigRepository _paymentMethods =
        Substitute.For<IPaymentMethodConfigRepository>();

    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly FakeClock _clock = new();

    private InvoiceService CreateService() =>
        new(_orders, _paymentMethods, _settings, _clock, NullLogger<InvoiceService>.Instance);

    [Fact]
    public async Task An_order_that_does_not_exist_has_no_invoice()
    {
        var result = await CreateService().RenderAsync("WH-2609-99999");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.OrderNotFound);
    }

    [Fact]
    public async Task A_blank_order_number_is_not_a_database_round_trip()
    {
        var result = await CreateService().RenderAsync("   ");

        result.IsSuccess.ShouldBeFalse();
        await _orders.DidNotReceive()
            .GetByNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_file_is_named_after_the_order()
    {
        // It lands in a folder of hundreds. "WH-2609-00042.pdf" sorts and
        // searches; "invoice.pdf (7)" does not.
        GivenTheOrder();

        var result = await CreateService().RenderAsync(InvoiceFixture.OrderNumber);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data.ShouldNotBeNull();
        result.Data.FileName.ShouldBe($"{InvoiceFixture.OrderNumber}.pdf");
        Encoding.ASCII.GetString(result.Data.Content, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task An_unnamed_shop_still_prints_an_invoice()
    {
        // Every store setting the header wants is unset — which is the state
        // the shop is in today. The van does not wait for a settings screen.
        GivenTheOrder();

        var result = await CreateService().RenderAsync(InvoiceFixture.OrderNumber);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data!.Content.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_payment_method_is_named_as_the_shop_named_it()
    {
        // The shop can rename its own methods, and the invoice follows.
        GivenTheOrder();

        _paymentMethods
            .GetByCodeAsync(PaymentMethodCodes.CashOnDelivery, Arg.Any<CancellationToken>())
            .Returns(new PaymentMethodConfig
            {
                Code = PaymentMethodCodes.CashOnDelivery,
                DisplayName = LocalizedText.Create("Cash on delivery", "ক্যাশ অন ডেলিভারি")
            });

        var result = await CreateService().RenderAsync(InvoiceFixture.OrderNumber);

        result.IsSuccess.ShouldBeTrue(result.Message);
        await _paymentMethods.Received()
            .GetByCodeAsync(PaymentMethodCodes.CashOnDelivery, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_method_row_that_has_since_been_deleted_does_not_stop_the_invoice()
    {
        // Orders placed through a method the shop later removed must still be
        // printable. The code stands in for the name rather than the payment
        // box coming out blank.
        GivenTheOrder();

        _paymentMethods
            .GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PaymentMethodConfig?)null);

        var result = await CreateService().RenderAsync(InvoiceFixture.OrderNumber);

        result.IsSuccess.ShouldBeTrue(result.Message);
    }

    private void GivenTheOrder()
    {
        // The static constructor on the fixture is what registers the fonts and
        // turns strict glyph checking on for this assembly.
        var order = InvoiceFixture.Order();

        _orders.GetByNumberAsync(InvoiceFixture.OrderNumber, Arg.Any<CancellationToken>())
            .Returns(order);
    }
}
