using NetArchTest.Rules;

namespace WoodHeart.Tests.Architecture;

public static class ArchTestExtensions
{
    /// <summary>
    /// Asserts a rule passed, naming every offending type.
    /// </summary>
    /// <remarks>
    /// NetArchTest's own failure message says only that the rule failed. Naming
    /// the types is the difference between a five-second fix and a hunt.
    /// </remarks>
    public static void ShouldBeSuccessful(this TestResult result, string because)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypes?.Select(t => t.FullName) ?? [];

        throw new ShouldAssertException(
            $"Architecture rule violated: {because}.{Environment.NewLine}"
            + $"Offending types:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", offenders));
    }
}
