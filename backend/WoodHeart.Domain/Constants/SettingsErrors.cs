namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for the store settings screen.
/// </summary>
/// <remarks>
/// Same contract as <see cref="OrderingErrors"/>: the client branches on the
/// code, the message is prose, and the suffix picks the HTTP status.
/// </remarks>
public static class SettingsErrors
{
    private const string Prefix = "settings.";

    /// <summary>
    /// A key that is not in the table. The screen edits the settings the
    /// shop has; it does not invent new ones — a typo in a key must not
    /// quietly create a setting nothing reads.
    /// </summary>
    public const string UnknownKey = Prefix + "key.not_found";

    /// <summary>
    /// One or more values failed their rule. The per-key messages are in
    /// <c>Errors</c>, keyed by the setting key, and nothing was saved.
    /// </summary>
    public const string InvalidValue = Prefix + "invalid_value";
}
