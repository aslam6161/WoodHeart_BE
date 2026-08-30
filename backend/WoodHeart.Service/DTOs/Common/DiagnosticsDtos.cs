using System.ComponentModel.DataAnnotations;

namespace WoodHeart.Service.DTOs.Common;

/// <summary>
/// The walking-skeleton request: proves routing, model binding, validation, the
/// service layer, the clock and the error contract all work end to end.
/// </summary>
public class EchoRequestDto
{
    [Required(ErrorMessage = "A message is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "The message must be 1 to 200 characters.")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional. Any of the four ways a Bangladeshi customer might type their number.</summary>
    public string? PhoneNumber { get; set; }
}

public class EchoResponseDto
{
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>The same instant in Dhaka. Should always carry a +06:00 offset.</summary>
    public DateTimeOffset ReceivedAtDhaka { get; set; }

    /// <summary>E.164, e.g. <c>+8801712345678</c>. Null when no number was supplied.</summary>
    public string? NormalizedPhone { get; set; }

    /// <summary>Safe for logs, e.g. <c>017****5678</c>.</summary>
    public string? MaskedPhone { get; set; }
}

public class PingResponseDto
{
    public string Status { get; set; } = "ok";

    public string Environment { get; set; } = string.Empty;

    public DateTimeOffset UtcNow { get; set; }

    public DateTimeOffset DhakaNow { get; set; }

    public string Version { get; set; } = string.Empty;
}
