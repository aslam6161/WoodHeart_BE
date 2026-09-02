using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WoodHeart.Presentation.Extensions;

/// <summary>
/// The OpenAPI document behind Scalar at <c>/scalar/v1</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered only in Development — see <c>Program.cs</c>. An unauthenticated,
/// fully documented map of the API surface is not something to publish.
/// </para>
/// <para>
/// <b>Why the security scheme matters here.</b> Without it Scalar renders no
/// token field, so every <c>/api/admin</c> endpoint answers 401 and there is no
/// way to try one from the browser. The document is then a reference for the
/// public catalog and useless for the half of the API that is harder to call by
/// hand.
/// </para>
/// </remarks>
public static class OpenApiExtension
{
    private const string SchemeName = "bearerAuth";

    public static IServiceCollection AddWoodHeartOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "WoodHeart API",
                    Version = "v1",
                    Description =
                        "Storefront and admin API. Sign in through /api/account/login, then paste "
                        + "the accessToken into Authorize above — the admin endpoints need it."
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

                document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,

                    // Lower-case "bearer" is not a style choice: the OpenAPI
                    // specification defines the scheme name against the HTTP
                    // Authentication Scheme registry, which is lower-case.
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "The accessToken from /api/account/login. No \"Bearer \" prefix."
                };

                return Task.CompletedTask;
            });

            // Per operation rather than one document-wide requirement.
            //
            // A global requirement puts a padlock on every endpoint including
            // the public catalog, which tells a reader that browsing products
            // needs a token. It does not, and that is the single most important
            // thing this document communicates about the storefront.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;

                // AllowAnonymous wins, exactly as it does at runtime.
                // CatalogController carries both an explicit [AllowAnonymous]
                // and, in future, whatever global fallback policy is added.
                var anonymous = metadata.OfType<IAllowAnonymous>().Any();
                var authorized = metadata.OfType<IAuthorizeData>().Any();

                if (anonymous || !authorized)
                {
                    return Task.CompletedTask;
                }

                operation.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SchemeName)] = []
                    }
                ];

                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd(
                    "401", new OpenApiResponse { Description = "No token, or the token has expired." });
                operation.Responses.TryAdd(
                    "403", new OpenApiResponse { Description = "Signed in, but not with the required role." });

                return Task.CompletedTask;
            });
        });
}
