using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Payments;

namespace WoodHeart.Service.Interfaces.Payments;

/// <summary>
/// Configuring how the shop takes money.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the screen that makes turning bKash on a toggle rather than a
/// release.</b> Which methods a customer sees, in what order, under what
/// conditions, against sandbox or live, and with what credentials — all of it
/// is a row, and this is what edits the row.
/// </para>
/// <para>
/// <b>There is no create and no delete.</b> A method's code is the join to the
/// <see cref="IPaymentProvider"/> that implements it, and the resolver offers
/// only rows where both exist. A row invented from a screen would be one that
/// looks configured, reads as enabled, and never appears at checkout — with
/// nothing anywhere to say why. Methods arrive with the class that serves them.
/// </para>
/// <para>
/// <b>Credentials go in and never come out.</b> They are encrypted at rest and
/// are on no DTO, not even this one's: the admin is told whether something is
/// stored and offered to replace it.
/// </para>
/// </remarks>
public interface IPaymentMethodAdminService
{
    /// <summary>Every method, enabled or not, in the shop's display order.</summary>
    Task<GeneralResponse<IReadOnlyList<PaymentMethodConfigDto>>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<PaymentMethodConfigDto>> GetAsync(
        string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves one method whole.
    /// </summary>
    /// <remarks>
    /// Refused rather than quietly accepted in three cases, each of which would
    /// otherwise produce a method that reads as on and does nothing: enabling
    /// one nothing implements, enabling one that needs a credential and has
    /// none, and enabling one available in neither zone.
    /// </remarks>
    Task<GeneralResponse<PaymentMethodConfigDto>> UpdateAsync(
        string code, UpdatePaymentMethodDto dto, CancellationToken cancellationToken = default);
}
