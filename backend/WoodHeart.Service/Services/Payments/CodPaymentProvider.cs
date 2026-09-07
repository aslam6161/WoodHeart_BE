using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Payments;

namespace WoodHeart.Service.Services.Payments;

/// <summary>
/// Cash on delivery. The method most of this shop's orders will use.
/// </summary>
/// <remarks>
/// <para>
/// There is no gateway here and no external call, which is exactly why it is
/// written as a provider rather than as an <c>if</c> in the checkout: the
/// checkout stays written against one port, and bKash arrives as a second class
/// rather than as a second branch through every method.
/// </para>
/// <para>
/// <b>The state is <see cref="PaymentState.AwaitingCollection"/>, not
/// Succeeded.</b> The order is confirmed and the goods will ship, but no money
/// has moved — and a shop that marks COD as paid at placement cannot answer
/// "how much cash is out with riders right now", which is the question that
/// matters most in a cash market.
/// </para>
/// </remarks>
public class CodPaymentProvider : IPaymentProvider
{
    public string Code => PaymentMethodCodes.CashOnDelivery;

    public PaymentCapabilities Capabilities { get; } = new(
        SupportsRedirect: false,
        // Money handed back at the door is not a gateway refund. It is recorded
        // against the order by staff, so claiming support here would put a
        // button on an admin screen that cannot do anything.
        SupportsRefund: false,
        SupportsWebhook: false,
        SettlesImmediately: true);

    public Task<GeneralResponse<InitiateResult>> InitiateAsync(
        PaymentContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(GeneralResponse<InitiateResult>.Success(
            new InitiateResult(PaymentState.AwaitingCollection, Reference: null, RedirectUrl: null)));

    public Task<GeneralResponse<ExecuteResult>> ExecuteAsync(
        string reference, CancellationToken cancellationToken = default) =>
        NotSupported<ExecuteResult>(
            "Cash on delivery is collected by the rider, not completed online.");

    public Task<GeneralResponse<PaymentStatusResult>> QueryAsync(
        string reference, CancellationToken cancellationToken = default) =>
        // Deliberately not "Succeeded because the order says Delivered". The
        // truth about cash lives in what the rider handed in, and inventing an
        // answer here would make reconciliation agree with itself rather than
        // with the money.
        NotSupported<PaymentStatusResult>("There is no gateway to ask about a cash payment.");

    public Task<GeneralResponse<RefundResult>> RefundAsync(
        RefundRequest request, CancellationToken cancellationToken = default) =>
        NotSupported<RefundResult>("A cash refund is recorded against the order by staff.");

    private static Task<GeneralResponse<T>> NotSupported<T>(string message) =>
        Task.FromResult(GeneralResponse<T>.Fail(PaymentErrors.OperationNotSupported, message));
}
