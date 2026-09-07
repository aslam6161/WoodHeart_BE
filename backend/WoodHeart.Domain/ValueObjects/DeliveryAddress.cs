namespace WoodHeart.Domain.ValueObjects;

/// <summary>
/// Where an order is going, in the shape Bangladeshi addresses actually take.
/// </summary>
/// <remarks>
/// <para>
/// Division → district → upazila → area, then a free-text line. That hierarchy
/// is not decoration: it is how a rider is told where to go, how the delivery
/// zone is decided, and how "which districts do we sell most to" is answerable
/// later. A single <c>Address</c> textarea would make all three impossible.
/// </para>
/// <para>
/// <b><see cref="Landmark"/> is not a nicety here.</b> Street numbering is
/// inconsistent across much of the country and riders navigate by "beside the
/// Jame Masjid, third building". Leaving it out means a phone call on every
/// delivery.
/// </para>
/// <para>
/// Stored as an owned type on the order — copied in, not referenced — so that a
/// customer editing their saved address next year does not rewrite where last
/// year's order was sent.
/// </para>
/// <para>
/// The setters are <c>init</c> rather than private because EF materialises
/// these directly. <see cref="Create"/> is the checked way in and is what every
/// caller in the application uses.
/// </para>
/// </remarks>
public sealed class DeliveryAddress : ValueObject
{
    public const int MaxDivision = 60;
    public const int MaxDistrict = 60;
    public const int MaxUpazila = 80;
    public const int MaxArea = 120;
    public const int MaxAddressLine = 400;
    public const int MaxLandmark = 200;
    public const int MaxPostcode = 10;

    public string Division { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    /// <summary>Upazila or thana. Optional inside a city, where the area is enough.</summary>
    public string? Upazila { get; init; }

    /// <summary>"Dhanmondi", "Bashundhara R/A" — the neighbourhood a rider knows.</summary>
    public string? Area { get; init; }

    /// <summary>House, road and any flat number, as the customer wrote it.</summary>
    public string AddressLine { get; init; } = string.Empty;

    /// <summary>"Opposite Popular Diagnostic". How the rider actually finds it.</summary>
    public string? Landmark { get; init; }

    public string? Postcode { get; init; }

    /// <summary>
    /// Builds an address, trimming and normalising blanks to null.
    /// </summary>
    /// <remarks>
    /// Whitespace-only optional fields become null rather than <c>" "</c>, so
    /// that "has this customer given a landmark?" is a null check and not a
    /// string-trimming exercise repeated at every call site.
    /// </remarks>
    public static DeliveryAddress Create(
        string division,
        string district,
        string addressLine,
        string? upazila = null,
        string? area = null,
        string? landmark = null,
        string? postcode = null) =>
        new()
        {
            Division = Required(division, nameof(division)),
            District = Required(district, nameof(district)),
            AddressLine = Required(addressLine, nameof(addressLine)),
            Upazila = Optional(upazila),
            Area = Optional(area),
            Landmark = Optional(landmark),
            Postcode = Optional(postcode)
        };

    /// <summary>
    /// One line, for an SMS or the top of an invoice.
    /// </summary>
    /// <remarks>
    /// Ordered narrowest-first — house, then area, then district — because that
    /// is how the address is read aloud to a rider, and because the first
    /// forty characters of an SMS are the ones that fit on a phone's preview.
    /// </remarks>
    public string ToSingleLine()
    {
        string?[] parts = [AddressLine, Landmark, Area, Upazila, District, Division, Postcode];

        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public override string ToString() => ToSingleLine();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Division;
        yield return District;
        yield return Upazila;
        yield return Area;
        yield return AddressLine;
        yield return Landmark;
        yield return Postcode;
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required on a delivery address.", name)
            : value.Trim();

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
