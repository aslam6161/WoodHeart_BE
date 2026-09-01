namespace WoodHeart.Domain.Settings;

/// <summary>
/// Cloudinary credentials and upload policy.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ApiSecret"/> never appears in <c>appsettings.json</c>.</b> It
/// ships empty and is supplied through user-secrets locally and an environment
/// variable in production, the same way <c>Jwt:SigningKey</c> is. Anyone
/// holding it can upload to, transform and permanently destroy every asset in
/// the account.
/// </para>
/// <para>
/// <see cref="CloudName"/> is not a secret and is treated differently on
/// purpose — it appears in the host of every delivery URL the storefront
/// renders, so the Angular app carries its own copy rather than asking the API
/// what it is.
/// </para>
/// </remarks>
public class CloudinarySettings
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// The folder every asset is written under, e.g. <c>woodheart</c>.
    /// </summary>
    /// <remarks>
    /// Set it per environment. Sharing one folder between staging and
    /// production means a staging tidy-up destroys live product photography.
    /// </remarks>
    public string Folder { get; set; } = "woodheart";

    /// <summary>
    /// Largest image the API will accept and forward, in bytes.
    /// </summary>
    /// <remarks>
    /// Images go through this server, so this is also the ceiling on what one
    /// request can make it buffer. A phone photograph is 3–8 MB; 12 MB leaves
    /// room without inviting someone to stream a film through it. Video does
    /// not use this path at all — see <c>CreateVideoUploadTicketAsync</c>.
    /// </remarks>
    public long MaxImageBytes { get; set; } = 12 * 1024 * 1024;

    /// <summary>How long a signed direct-upload ticket stays valid.</summary>
    /// <remarks>
    /// Short, because a leaked ticket is an upload slot on the account. Long
    /// enough that a 200 MB video over a Dhaka office connection still finishes
    /// — Cloudinary validates the signature when the upload *starts*.
    /// </remarks>
    public int UploadTicketMinutes { get; set; } = 10;

    /// <summary>True when enough is configured to talk to Cloudinary at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret);
}
