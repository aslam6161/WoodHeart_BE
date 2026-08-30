using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WoodHeart.Service.DTOs.Common;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// The whole request pipeline, end to end, in memory: routing, model binding,
/// the validation filter, the service layer, the error contract and the
/// correlation id.
/// </summary>
public class PipelineTests(WoodHeartApiFactory factory) : IClassFixture<WoodHeartApiFactory>
{
    private static readonly JsonSerializerOptions Json =
        new() { PropertyNameCaseInsensitive = true };

    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Liveness_does_not_touch_the_database()
    {
        // Deliberately reachable with no Postgres running: a database outage
        // should page someone, not make the orchestrator kill the container.
        var response = await Client.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_reports_Dhaka_time_six_hours_ahead_of_UTC()
    {
        var response = await Client.GetAsync("/api/diagnostics/ping");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PingResponseDto>(Json);

        body.ShouldNotBeNull();
        body!.DhakaNow.Offset.ShouldBe(TimeSpan.FromHours(6));
        body.Status.ShouldBe("ok");
    }

    [Fact]
    public async Task A_valid_echo_normalises_the_phone_number()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/diagnostics/echo",
            new { message = "hello", phoneNumber = "017-1234-5678" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");

        data.GetProperty("normalizedPhone").GetString().ShouldBe("+8801712345678");
        data.GetProperty("maskedPhone").GetString().ShouldBe("017****5678");
    }

    [Fact]
    public async Task An_empty_message_returns_400_with_per_field_errors()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/diagnostics/echo",
            new { message = "", phoneNumber = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("isSuccess").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("errorCode").GetString().ShouldBe("common.validation_failed");

        // camelCase, so the Angular reactive form can map errors.message straight
        // onto the control without a translation table.
        document.RootElement.GetProperty("errors").TryGetProperty("message", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_business_failure_returns_a_stable_error_code()
    {
        // 011 is a retired operator prefix. This is a domain rule, not a model
        // binding rule, so it comes back from the service — and it must still
        // arrive in the same response shape as a validation failure.
        var response = await Client.PostAsJsonAsync(
            "/api/diagnostics/echo",
            new { message = "hi", phoneNumber = "01112345678" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("errorCode").GetString().ShouldBe("common.invalid_phone");
    }

    [Fact]
    public async Task An_inbound_correlation_id_is_honoured_and_echoed_back()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/diagnostics/ping");
        request.Headers.Add("X-Correlation-Id", "trace-me-please");

        var response = await Client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").ShouldContain("trace-me-please");
    }

    [Fact]
    public async Task A_correlation_id_is_generated_when_the_client_sends_none()
    {
        var response = await Client.GetAsync("/api/diagnostics/ping");

        response.Headers.TryGetValues("X-Correlation-Id", out var values).ShouldBeTrue();
        values.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Json_is_serialised_in_camelCase()
    {
        var response = await Client.GetAsync("/api/diagnostics/ping");
        var body = await response.Content.ReadAsStringAsync();

        // Case.Sensitive matters: Shouldly's string assertions are
        // case-INSENSITIVE by default, which makes a casing test pass vacuously.
        body.ShouldContain("dhakaNow", Case.Sensitive);
        body.ShouldNotContain("DhakaNow", Case.Sensitive);
    }

    [Fact]
    public async Task An_unknown_route_returns_404_rather_than_an_exception()
    {
        var response = await Client.GetAsync("/api/diagnostics/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
