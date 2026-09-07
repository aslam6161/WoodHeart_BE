using System.Globalization;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Services.Common;

/// <summary>
/// Builds the numbers customers and couriers quote: <c>WH-2609-00042</c>.
/// </summary>
/// <remarks>
/// <para>
/// Three parts, each earning its place. The prefix says whose it is on a
/// delivery slip with four shops' paperwork on it. The period says when,
/// so a number read over the phone can be found without a date. The counter is
/// zero-padded to five so the numbers align in a column and sort as text.
/// </para>
/// <para>
/// <b>The period is Dhaka's month, not UTC's.</b> An order placed at 02:00 on
/// the first of October in Dhaka is 20:00 on the thirtieth of September in UTC,
/// and numbering it <c>2609</c> would put it in the wrong month's sales — a
/// six-hour error that stays invisible until someone reconciles a month end.
/// </para>
/// </remarks>
public class NumberSequenceService(
    INumberSequenceRepository sequences,
    IDateTimeProvider clock) : INumberSequenceService
{
    /// <summary>Sequence name for orders.</summary>
    public const string Orders = "order";

    /// <summary>
    /// Five digits: 99,999 orders in one month before it widens.
    /// </summary>
    /// <remarks>
    /// It widens rather than wraps — the format string pads to five and prints
    /// six if it has to, so passing the limit produces a longer number, not a
    /// duplicate one.
    /// </remarks>
    private const string CounterFormat = "00000";

    public async Task<string> NextAsync(
        string sequenceName, string prefix, CancellationToken cancellationToken = default)
    {
        var period = clock.DhakaNow.ToString("yyMM", CultureInfo.InvariantCulture);

        var value = await sequences.ReserveNextAsync(sequenceName, period, cancellationToken);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{period}-{value.ToString(CounterFormat, CultureInfo.InvariantCulture)}");
    }
}
