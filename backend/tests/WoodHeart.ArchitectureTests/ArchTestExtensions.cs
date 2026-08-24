using System.Text;
using NetArchTest.Rules;

namespace WoodHeart.ArchitectureTests;

/// <summary>
/// Turns a NetArchTest result into a failure message that names the offending
/// types and explains why the rule exists.
/// </summary>
/// <remarks>
/// The default assertion says only "expected true, was false", which tells a
/// developer nothing about what they broke or why the rule is there. Since the
/// whole point of these tests is to teach the architecture at the moment
/// someone bumps into it, the message has to carry that.
/// </remarks>
internal static class ArchTestExtensions
{
    public static void ShouldBeSuccessful(this TestResult result, string because)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        var message = new StringBuilder()
            .AppendLine("Architecture rule violated.")
            .AppendLine()
            .Append("Rule: ").AppendLine(because)
            .AppendLine()
            .AppendLine("Offending types:");

        foreach (var type in result.FailingTypes ?? [])
        {
            message.Append("  • ").AppendLine(type.FullName ?? type.Name);
        }

        message
            .AppendLine()
            .AppendLine("The fix is almost never to relax this test — it is to move the code")
            .AppendLine("into the layer it belongs in, or to introduce a port for it.");

        throw new ArchitectureRuleViolationException(message.ToString());
    }
}

internal sealed class ArchitectureRuleViolationException(string message) : Exception(message);
