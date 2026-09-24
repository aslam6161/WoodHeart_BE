using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// What the shop is offering at the moment.
/// </summary>
/// <remarks>
/// <para>
/// The storefront asks this once so it can leave out what is switched off,
/// rather than showing a link that fails when somebody follows it. The server
/// still refuses the work itself — this is so the shop looks deliberate, not
/// so the rule is enforced here.
/// </para>
/// <para>
/// <c>[AllowAnonymous]</c> because it is read before anybody signs in, and
/// because there is nothing here worth protecting: it says what the shop sells,
/// which is the one thing a shop wants known.
/// </para>
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/features")]
public class FeaturesController(IFeatureFlagService features) : BaseApiController
{
    /// <summary>The switches the storefront needs to draw itself.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        HandleResult(
            GeneralResponse<StorefrontFeaturesDto>.Success(
                new StorefrontFeaturesDto
                {
                    Consultations = await features.IsEnabledAsync(
                        FeatureFlags.ConsultationsEnabled, cancellationToken)
                }));
}

/// <summary>
/// What the storefront may show.
/// </summary>
/// <remarks>
/// Named for what the reader may do rather than for the flag behind it, so the
/// storefront is not coupled to the spelling of a row in a table.
/// </remarks>
public class StorefrontFeaturesDto
{
    /// <summary>Whether to offer consultation booking at all.</summary>
    public bool Consultations { get; init; }
}
