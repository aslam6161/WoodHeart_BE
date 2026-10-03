using WoodHeart.Repository;

namespace WoodHeart.Service.Interfaces.Consultations;

/// <summary>
/// One piece of furniture, as the model is allowed to see it.
/// </summary>
/// <remarks>
/// Deliberately not the product entity. What goes to a third party is what a
/// customer could read off the website anyway — no cost price, no supplier, no
/// stock position, no internal code. The <see cref="Slug"/> is the only
/// identifier, and it is already in every product URL.
/// </remarks>
public record CataloguePiece(
    string Slug,
    string Name,
    string Category,
    decimal FromPrice,
    string MadeTo,
    string? Blurb);

/// <summary>
/// What the model answered: prose, and which pieces it pointed at.
/// </summary>
/// <remarks>
/// Slugs rather than descriptions, because the storefront renders the pieces
/// itself from the database. The model decides <i>which</i>; the shop's own
/// data decides what they cost and whether they can be bought.
/// </remarks>
public record DesignAnswer(string Reply, IReadOnlyList<string> Slugs);

/// <summary>
/// The adviser behind the consultation page.
/// </summary>
/// <remarks>
/// <para>
/// A port, for the same reason <c>ISmsSender</c> is one: the provider is a
/// commercial decision that will change. Everything above this talks to this
/// interface, and only one class knows whose model is answering.
/// </para>
/// <para>
/// It is also the seam where the feature switches off. With no credentials the
/// registered implementation says so politely and nothing else in the
/// application has to know why.
/// </para>
/// </remarks>
public interface IDesignAssistant
{
    /// <summary>Whether this implementation can actually answer.</summary>
    bool IsAvailable { get; }

    Task<GeneralResponse<DesignAnswer>> AskAsync(
        string question,
        IReadOnlyList<CataloguePiece> catalogue,
        CancellationToken cancellationToken = default);
}
