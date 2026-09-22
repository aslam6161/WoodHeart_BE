using Hangfire;

namespace WoodHeart.Service.Interfaces.Jobs;

/// <summary>
/// Cancels orders that went to a payment gateway and never came back, and
/// frees the stock they were holding.
/// </summary>
/// <remarks>
/// <para>
/// The hold is the point. An order reserves its units the moment it is
/// placed, so two customers cannot both buy the last bed; the price of that
/// is a customer who closes the bKash tab keeping a bed off the shelf. This
/// job is where that hold ends.
/// </para>
/// <para>
/// Cash on delivery is confirmed at placement and is never Pending here.
/// Until a gateway is switched on the job finds nothing, and that is fine —
/// it has to exist before the first gateway order does, not after.
/// </para>
/// </remarks>
public interface IUnpaidOrderExpiry
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Once a morning: tells the shop which lines are at or below their reorder
/// level, by SMS to the shop phone and email to the shop address.
/// </summary>
/// <remarks>
/// One message a day, idempotent on the date — a second run on the same
/// morning, after a restart, sends nothing. Nothing is sent when nothing is
/// low, so the message means something when it arrives.
/// </remarks>
public interface ILowStockDigest
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}
