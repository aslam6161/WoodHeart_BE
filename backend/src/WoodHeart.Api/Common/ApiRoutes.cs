namespace WoodHeart.Api.Common;

/// <summary>
/// Route prefixes for the public API.
/// </summary>
/// <remarks>
/// <para>
/// Versioning is a URL segment (<c>/api/v1/products</c>) rather than a header
/// or a media type. It is the version scheme a developer can see in a browser
/// address bar, curl by hand, and cache at the CDN — all of which matter more
/// here than the theoretical purity of content negotiation.
/// </para>
/// <para>
/// A breaking change introduces <c>/api/v2</c> alongside v1; the Angular client
/// pins exactly one. A full versioning library (<c>Asp.Versioning</c>) is worth
/// adding the day a second version actually exists, and not before.
/// </para>
/// </remarks>
public static class ApiRoutes
{
    public const string V1 = "api/v1";

    /// <summary>Admin endpoints, all of which sit behind a role check.</summary>
    public const string AdminV1 = "api/v1/admin";
}
