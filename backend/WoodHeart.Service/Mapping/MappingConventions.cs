using Riok.Mapperly.Abstractions;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Service.Mapping;

/// <summary>
/// Conversions every mapper in the application shares.
/// </summary>
/// <remarks>
/// <para>
/// Where an AutoMapper codebase would put a <c>Profile</c>, this codebase puts
/// a <c>[Mapper]</c> partial class: declare the signature, and the generator
/// writes the assignments at compile time. A missing or mistyped property is a
/// build error rather than a runtime <c>MapperConfigurationException</c>.
/// </para>
/// <para>
/// Value objects need explicit conversions because they have private
/// constructors — which is the point of them. A <see cref="Money"/> flattens to
/// its amount for the wire, and the currency travels once on the response
/// rather than on every line.
/// </para>
/// </remarks>
[Mapper]
public static partial class MappingConventions
{
    /// <summary>Money to the wire: the amount only.</summary>
    public static decimal ToAmount(Money? money) => money?.Amount ?? 0m;

    /// <summary>Phone to the wire: E.164, the stored form.</summary>
    public static string? ToE164(PhoneNumber? phone) => phone?.Value;

    /// <summary>Phone to a log or an admin list: masked.</summary>
    public static string? ToMasked(PhoneNumber? phone) => phone?.Masked;

    /// <summary>Localised text in the caller's language, falling back to English.</summary>
    public static string ToDisplay(LocalizedText? text, string language) =>
        text is null ? string.Empty : text.For(language);
}
