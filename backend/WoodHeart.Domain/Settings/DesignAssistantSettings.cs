namespace WoodHeart.Domain.Settings;

/// <summary>
/// The language model behind the design assistant.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ApiKey"/> never appears in <c>appsettings.json</c>.</b> It
/// ships empty and is supplied through user-secrets locally and an environment
/// variable in production, like every other spending credential here. Anyone
/// holding it can spend this shop's quota.
/// </para>
/// <para>
/// Groq speaks the OpenAI chat-completions protocol, which is why there is no
/// vendor SDK anywhere in this codebase — it is one POST with a JSON body. That
/// also means moving to another provider that speaks it is a change to
/// <see cref="BaseUrl"/> and <see cref="Model"/>, not a rewrite.
/// </para>
/// <para>
/// With no key the assistant is simply off: the endpoint says so and the
/// storefront does not offer it. An unavailable adviser is a missing
/// convenience, not a broken shop, so it must never stop the API booting.
/// </para>
/// </remarks>
public class DesignAssistantSettings
{
    public const string SectionName = "DesignAssistant";

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/";

    /// <summary>
    /// The model id, exactly as the provider names it.
    /// </summary>
    /// <remarks>
    /// Configurable because a hosted model is retired on the provider's
    /// schedule, not the shop's. When that happens the call fails with a
    /// message naming the model, and fixing it is a configuration change rather
    /// than a deployment.
    /// </remarks>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// How long to wait for an answer before giving up.
    /// </summary>
    /// <remarks>
    /// A customer waiting on a page will not wait a minute, and a request that
    /// is still open is a thread this server is not using for an order.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// The ceiling on a reply, in tokens.
    /// </summary>
    /// <remarks>
    /// Three short paragraphs. The assistant's job is to point at furniture,
    /// not to write an essay about it — and the pieces themselves are rendered
    /// by the storefront from the database, not described in the prose.
    /// </remarks>
    public int MaxReplyTokens { get; set; } = 700;

    /// <summary>
    /// How many products are put in front of the model.
    /// </summary>
    /// <remarks>
    /// The whole catalogue, while it is small enough to fit — which for this
    /// shop it is, by two orders of magnitude. Beyond this many, the most
    /// relevant are selected by the ordinary catalogue search first, because a
    /// prompt that does not fit is answered by a model that has quietly
    /// forgotten the middle of it.
    /// </remarks>
    public int MaxCatalogueItems { get; set; } = 60;

    /// <summary>
    /// Whether the assistant can answer at all.
    /// </summary>
    /// <remarks>
    /// The key is the whole test. A base URL and a model name without one are
    /// settings for a call that will be rejected.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Model);
}
