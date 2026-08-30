namespace WoodHeart.Presentation.Logging;

/// <summary>
/// Source-generated startup logging.
/// </summary>
/// <remarks>
/// <c>ILogger</c> is qualified because <c>Program.cs</c> has both
/// <c>Serilog</c> and <c>Microsoft.Extensions.Logging</c> in scope, and each
/// declares a type by that name.
/// </remarks>
internal static partial class StartupLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "Seeding failed.")]
    public static partial void SeedFailed(
        Microsoft.Extensions.Logging.ILogger logger, Exception exception);
}
