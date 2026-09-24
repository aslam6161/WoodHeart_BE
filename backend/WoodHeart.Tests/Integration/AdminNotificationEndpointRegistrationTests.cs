using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the notification routes exist, that the right people can reach each of
/// them, and that nothing here invents a template or edits a log.
/// </summary>
/// <remarks>
/// <para>
/// The split is the point. <b>Reading is for all staff</b> — whoever answers
/// the telephone is the person asked "I never got a text about my order", and a
/// screen they cannot open is a question they cannot answer. <b>The switches
/// are Admin</b>, because turning a template off is a decision about what the
/// shop pays a gateway, which belongs with the settings and payment screens
/// rather than with the day's orders.
/// </para>
/// <para>
/// Both halves are worth a test rather than a reading of the attributes.
/// Tightening the read would quietly break the telephone; loosening the write
/// would let anyone with a till login stop every customer being told about
/// their order, with nothing on any screen to say who did it.
/// </para>
/// </remarks>
public class AdminNotificationEndpointRegistrationTests(WoodHeartApiFactory factory)
    : IClassFixture<WoodHeartApiFactory>
{
    private IEnumerable<RouteEndpoint> Endpoints =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    private RouteEndpoint? Find(string method, string pattern) =>
        Endpoints.FirstOrDefault(e =>
            string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase)
            && (e.Metadata.GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

    [Theory]
    [InlineData("GET", "api/admin/notifications/templates")]
    [InlineData("GET", "api/admin/notifications/templates/{code}")]
    [InlineData("GET", "api/admin/notifications/messages")]
    [InlineData("POST", "api/admin/notifications/messages/{id:long}/resend")]
    public void Anyone_on_the_staff_may_look_and_may_send_one_again(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");

        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();

        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldContain(
                a => a.Policy == Policies.RequireStaff,
                $"{method} /{pattern} must be reachable by staff");

        // Sending the same message to the same person again is answering a
        // question, not making a new decision about what the shop sends.
        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldNotContain(
                a => a.Policy == Policies.RequireAdmin,
                $"{method} /{pattern} must not be narrowed to the owner");
    }

    [Fact]
    public void But_only_the_owner_decides_what_the_shop_sends()
    {
        var endpoint = Find("PUT", "api/admin/notifications/templates/{code}");

        endpoint.ShouldNotBeNull("PUT on a template is not routed");

        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldContain(
                a => a.Policy == Policies.RequireAdmin,
                "turning a template off must be Admin-only");
    }

    [Theory]
    [InlineData("POST", "templates")]
    [InlineData("DELETE", "templates")]
    public void Nothing_here_creates_or_removes_a_kind_of_message(string method, string segment)
    {
        // A template's code is the join to the method that renders it. A row
        // invented from a screen would read as configured and send nothing.
        Endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith(
                $"api/admin/notifications/{segment}", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()
                ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) == true)
            .ShouldBeEmpty($"{method} on a template must not be routed");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void And_the_message_log_cannot_be_edited(string method)
    {
        // It is a record of what the shop did and did not say. A log an admin
        // can tidy is not one.
        Endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith(
                "api/admin/notifications/messages", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()
                ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) == true)
            .ShouldBeEmpty($"{method} on a message must not be routed");
    }

    [Fact]
    public void The_service_is_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<INotificationAdminService>().ShouldNotBeNull();
    }
}
