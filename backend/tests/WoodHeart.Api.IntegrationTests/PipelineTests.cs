using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace WoodHeart.Api.IntegrationTests;

/// <summary>
/// Verifies the request pipeline end to end: routing, validation, Result
/// mapping, problem-details shape, and correlation.
/// </summary>
/// <remarks>
/// These are the guarantees every later feature is built on. If they hold, a
/// broken endpoint is a bug in that endpoint; if they break, everything is
/// subtly wrong at once — which is why they are worth testing on their own.
/// </remarks>
public class PipelineTests(WoodHeartApiFactory factory) : IClassFixture<WoodHeartApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Liveness_endpoint_answers_without_touching_the_database()
    {
        // Must not depend on Postgres: a failing dependency should not make an
        // orchestrator kill an otherwise healthy process.
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_returns_dhaka_time_six_hours_ahead_of_utc()
    {
        var response = await _client.GetAsync("/api/v1/diagnostics/ping");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var utc = body.GetProperty("utc").GetDateTimeOffset();
        var dhaka = body.GetProperty("dhaka").GetDateTimeOffset();

        dhaka.Offset.ShouldBe(TimeSpan.FromHours(6));
        (dhaka - utc).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_valid_request_flows_through_the_pipeline_and_normalises_the_phone()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/diagnostics/echo", new
        {
            message = "hello",
            phoneNumber = "017-1234-5678"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("message").GetString().ShouldBe("hello");
        body.GetProperty("normalizedPhone").GetString().ShouldBe("+8801712345678");
        body.GetProperty("maskedPhone").GetString().ShouldBe("017****5678");
    }

    [Fact]
    public async Task Validation_failures_return_a_camelCased_per_field_error_dictionary()
    {
        // This exact shape is what Angular's reactive forms bind to, so it is a
        // contract with the frontend, not an implementation detail.
        var response = await _client.PostAsJsonAsync("/api/v1/diagnostics/echo", new
        {
            message = "",
            phoneNumber = (string?)null
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("type").GetString().ShouldBe("common.validation_failed");
        body.GetProperty("status").GetInt32().ShouldBe(400);

        var errors = body.GetProperty("errors");
        errors.TryGetProperty("message", out var messageErrors).ShouldBeTrue(
            "the field key must be camelCase to match the Angular form control name");
        messageErrors.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_domain_failure_returns_its_stable_machine_readable_code()
    {
        // The client branches on `type`, never on the English message — that
        // message will be translated to Bangla and must be free to change.
        var response = await _client.PostAsJsonAsync("/api/v1/diagnostics/echo", new
        {
            message = "hello",
            phoneNumber = "01112345678"   // retired 011 prefix
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("type").GetString().ShouldBe("common.invalid_phone");
        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("01712345678");
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        var response = await _client.GetAsync("/api/v1/diagnostics/ping");

        response.Headers.TryGetValues("X-Correlation-Id", out var values).ShouldBeTrue();
        values!.Single().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_inbound_correlation_id_is_honoured_rather_than_replaced()
    {
        // Lets a client tie a retry to its original attempt — essential when
        // debugging a duplicate-order report from a flaky mobile connection.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostics/ping");
        request.Headers.Add("X-Correlation-Id", "known-trace-id");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("known-trace-id");
    }

    [Fact]
    public async Task An_unknown_route_returns_404_and_not_an_exception_page()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Json_is_serialised_in_camelCase()
    {
        var response = await _client.GetAsync("/api/v1/diagnostics/ping");
        var raw = await response.Content.ReadAsStringAsync();

        // Case.Sensitive is essential here — Shouldly compares strings
        // case-insensitively by default, which would make a casing test pass
        // no matter what the API returned.
        raw.ShouldContain("\"dhakaToday\"", Case.Sensitive);
        raw.ShouldNotContain("\"DhakaToday\"", Case.Sensitive);
    }
}
