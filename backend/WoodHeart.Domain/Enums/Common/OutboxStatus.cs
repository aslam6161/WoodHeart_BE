namespace WoodHeart.Domain.Enums.Common;

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,

    /// <summary>Retries exhausted. Needs a human — surfaced on the admin dashboard.</summary>
    Failed = 3,

    /// <summary>Deliberately skipped, e.g. its notification template was disabled.</summary>
    Suppressed = 4
}

public enum SettingValueType
{
    String = 0,
    Integer = 1,
    Decimal = 2,
    Boolean = 3,
    Json = 4
}
