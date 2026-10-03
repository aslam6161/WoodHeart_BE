using System.ComponentModel.DataAnnotations;
using WoodHeart.Service.DTOs.Catalog;

namespace WoodHeart.Service.DTOs.Consultations;

/// <summary>One thing already said in this conversation.</summary>
/// <remarks>
/// The browser keeps the conversation and sends back the tail of it. Nothing is
/// stored here: a chat about a wardrobe is not worth a table, a session or a
/// retention policy, and the one place history is genuinely needed — "what
/// about the other one?" — is satisfied by the last few turns travelling with
/// the question.
/// </remarks>
public class ShopChatTurnDto
{
    /// <summary>True when the customer said it, false when the assistant did.</summary>
    public bool FromCustomer { get; set; }

    [StringLength(2000)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>A question for the shop assistant.</summary>
public class ShopChatRequestDto
{
    /// <summary>The question, in English or Bangla.</summary>
    [Required]
    [StringLength(600, MinimumLength = 2)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// What has been said so far, oldest first.
    /// </summary>
    /// <remarks>
    /// Capped on the server rather than trusted: this arrives from a browser
    /// and every turn in it is forwarded to a metered service. The limit is
    /// applied before the model sees it, not after the bill arrives.
    /// </remarks>
    public IReadOnlyList<ShopChatTurnDto> History { get; set; } = [];
}

/// <summary>
/// Something the customer can do next, if they want to.
/// </summary>
/// <remarks>
/// <b>A proposal, not an instruction.</b> The storefront renders this as a
/// button and runs its own code when it is pressed — the same cart code every
/// other button on the site runs. The assistant never puts anything in a
/// basket, so a model that misreads "I don't want the wardrobe" can offer the
/// wrong button but cannot buy a wardrobe.
/// </remarks>
public class ShopChatActionDto
{
    /// <summary>One of <c>ActionKinds</c>. Anything else never reaches here.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>What the button says, in the customer's own language.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The product it concerns, for the kinds that need one.</summary>
    public string? Slug { get; set; }
}

/// <summary>The assistant's answer: words, pieces, and what to offer next.</summary>
public class ShopChatReplyDto
{
    public string Reply { get; set; } = string.Empty;

    /// <summary>
    /// Read from the database after the model answered, never described by it.
    /// </summary>
    public IReadOnlyList<StorefrontProductDto> Products { get; set; } = [];

    public IReadOnlyList<ShopChatActionDto> Actions { get; set; } = [];
}
