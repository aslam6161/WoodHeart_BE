using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Settings;
using WoodHeart.Service.Infrastructure.Consultations;
using WoodHeart.Service.Interfaces.Consultations;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// What comes back from the model, and what the customer is shown.
/// </summary>
/// <remarks>
/// Every case here is a shape a real provider actually returned during a live
/// run against Groq, not one imagined at a desk. JSON mode guarantees valid
/// JSON; it guarantees nothing about the field names in it.
/// </remarks>
public class GroqDesignAssistantTests
{
    private static GroqDesignAssistant Assistant(HttpStatusCode status, string body)
    {
        var client = new HttpClient(new CannedHandler(status, body))
        {
            BaseAddress = new Uri("https://example.invalid/openai/v1/")
        };

        return new GroqDesignAssistant(
            client,
            Options.Create(new DesignAssistantSettings { ApiKey = "test-key" }),
            NullLogger<GroqDesignAssistant>.Instance);
    }

    /// <summary>Wraps a reply in the chat-completions envelope a provider sends.</summary>
    private static string Completion(string content)
    {
        var quoted = System.Text.Json.JsonSerializer.Serialize(content);

        // Concatenated rather than interpolated: the braces here are JSON, and
        // counting dollars against them is how this file stopped compiling.
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + quoted + "}}]}";
    }

    private static Task<WoodHeart.Repository.GeneralResponse<DesignAnswer>> Ask(
        HttpStatusCode status, string body) =>
        Assistant(status, body).AskAsync("What suits a small bedroom?", [
            new CataloguePiece("segun-king-bed", "Segun King Bed", "Beds", 45_000m, "in stock", null)
        ]);

    // -------------------------------------------------------------------------
    // The shape we asked for
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_reply_and_the_slugs_are_read_out_of_the_answer()
    {
        var result = await Ask(HttpStatusCode.OK,
            Completion("""{"reply":"The Segun King Bed suits it.","slugs":["segun-king-bed"]}"""));

        result.IsSuccess.ShouldBeTrue();
        result.Data!.Reply.ShouldBe("The Segun King Bed suits it.");
        result.Data.Slugs.ShouldBe(["segun-king-bed"]);
    }

    // -------------------------------------------------------------------------
    // The shapes it returns instead
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_refusal_in_the_wrong_field_is_still_shown_as_a_sentence()
    {
        // Found in a browser, not at a desk. Asked to reveal its instructions
        // the model declines — correctly — but it declines as
        // {"error": "..."}, and the page showed the customer the braces.
        var result = await Ask(HttpStatusCode.OK,
            Completion("""{"error":"I'm sorry, but I can't comply with that request."}"""));

        result.Data!.Reply.ShouldBe("I'm sorry, but I can't comply with that request.");
        result.Data.Slugs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_prose_is_taken_over_the_label_when_the_fields_are_unfamiliar()
    {
        // The longest string is the one carrying the answer, whatever the model
        // decided to call it.
        var result = await Ask(HttpStatusCode.OK,
            Completion("""{"status":"ok","message":"A Segun King Bed would suit that room well."}"""));

        result.Data!.Reply.ShouldBe("A Segun King Bed would suit that room well.");
    }

    [Fact]
    public async Task An_object_with_nothing_readable_in_it_becomes_an_apology()
    {
        var result = await Ask(HttpStatusCode.OK, Completion("""{"slugs":[],"count":0}"""));

        result.Data!.Reply.ShouldNotContain("{");
        result.Data.Reply.ShouldContain("try again");
    }

    [Fact]
    public async Task Plain_prose_that_ignored_the_format_is_still_an_answer()
    {
        var result = await Ask(HttpStatusCode.OK,
            Completion("A Segun King Bed would suit that room well."));

        result.Data!.Reply.ShouldBe("A Segun King Bed would suit that room well.");
    }

    [Fact]
    public async Task A_broken_fragment_of_an_object_is_not_shown_to_anybody()
    {
        // Truncation mid-JSON, which is what a reply ceiling that is too low
        // produces. Half an object is not prose.
        var result = await Ask(HttpStatusCode.OK, Completion("""{"reply":"The Segun King Be"""));

        result.Data!.Reply.ShouldNotStartWith("{");
        result.Data.Reply.ShouldContain("try again");
    }

    // -------------------------------------------------------------------------
    // When the provider says no
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Being_rate_limited_is_its_own_answer()
    {
        // The free tier allows a few thousand tokens a minute and the whole
        // catalogue goes with every question, so a busy evening reaches this
        // before anything is actually wrong. "Try again in a moment" is true
        // and actionable; "something went wrong" is neither.
        var result = await Ask(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit reached"}}""");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceBusy);
        result.Message.ShouldContain("moment");
    }

    [Fact]
    public async Task A_retired_model_fails_as_the_provider_failing()
    {
        // This happened on the first live call: the model named in the defaults
        // had been withdrawn. It is a configuration change, and the log says
        // which model — but to the customer it is simply a failure.
        var result = await Ask(HttpStatusCode.NotFound,
            """{"error":{"message":"The model does not exist","code":"model_not_found"}}""");

        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceFailed);
        result.ErrorCode.ShouldStartWith("external.");
    }

    [Fact]
    public async Task An_empty_choice_is_a_failure_not_an_empty_answer()
    {
        var result = await Ask(HttpStatusCode.OK, """{"choices":[]}""");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceFailed);
    }

    [Fact]
    public async Task With_no_key_it_does_not_call_anybody()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, Completion("""{"reply":"hello"}"""));
        var assistant = new GroqDesignAssistant(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") },
            Options.Create(new DesignAssistantSettings { ApiKey = "" }),
            NullLogger<GroqDesignAssistant>.Instance);

        assistant.IsAvailable.ShouldBeFalse();

        var result = await assistant.AskAsync("anything", []);

        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceUnavailable);
        handler.Calls.ShouldBe(0);
    }

    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
