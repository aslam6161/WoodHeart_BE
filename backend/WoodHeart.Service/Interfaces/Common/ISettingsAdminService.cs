using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Common;

namespace WoodHeart.Service.Interfaces.Common;

/// <summary>
/// The admin's side of <see cref="IStoreSettingService"/>: reading the whole
/// table and writing to it.
/// </summary>
/// <remarks>
/// Kept apart from the read service on purpose. That one is a singleton with
/// a cache in front of it and sits on every checkout; this one is scoped,
/// uncached, and used a few times a year. Mixing them would put the write
/// path's unit of work inside a singleton.
/// </remarks>
public interface ISettingsAdminService
{
    /// <summary>Every setting, grouped for the screen: by category, then key.</summary>
    Task<GeneralResponse<IReadOnlyList<StoreSettingDto>>> GetAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates every value against its setting's type and rule, then writes
    /// them all or none of them. Answers with the table as it now stands.
    /// </summary>
    Task<GeneralResponse<IReadOnlyList<StoreSettingDto>>> UpdateAsync(
        UpdateSettingsDto dto, CancellationToken cancellationToken = default);
}
