using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Services.Common;

/// <summary>
/// Reads runtime settings, cached in memory.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton with its own DI scope per read, because settings
/// are read on nearly every checkout — the VAT rate, the delivery charge, the
/// free-delivery threshold — and a database round trip for each would be a
/// query per request for values that change a few times a year.
/// </para>
/// <para>
/// The cache is in-process. On a single instance that is exactly right; if the
/// API is ever scaled out, <see cref="Invalidate"/> needs to become a
/// distributed signal or the entries need a short absolute expiry, because one
/// instance would otherwise serve the old VAT rate after an admin changed it.
/// The five-minute expiry below bounds that window rather than relying on
/// invalidation alone.
/// </para>
/// </remarks>
public class StoreSettingService(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache) : IStoreSettingService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey(key), out string? cached))
        {
            return cached;
        }

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStoreSettingRepository>();

        var setting = await repository.GetByKeyAsync(key, cancellationToken);

        cache.Set(CacheKey(key), setting?.Value, CacheDuration);

        return setting?.Value;
    }

    public async Task<decimal> GetDecimalAsync(
        string key, decimal fallback = 0m, CancellationToken cancellationToken = default)
    {
        var raw = await GetStringAsync(key, cancellationToken);

        // InvariantCulture, not the ambient one: a Bangla or European locale
        // parses "1,50" as one and a half, and a delivery charge that changes
        // meaning with the server's locale is a bug that only shows up in
        // production.
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public async Task<int> GetIntAsync(
        string key, int fallback = 0, CancellationToken cancellationToken = default)
    {
        var raw = await GetStringAsync(key, cancellationToken);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public async Task<bool> GetBoolAsync(
        string key, bool fallback = false, CancellationToken cancellationToken = default)
    {
        var raw = await GetStringAsync(key, cancellationToken);

        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    public void Invalidate(string key) => cache.Remove(CacheKey(key));

    private static string CacheKey(string key) => $"setting:{key}";
}

/// <summary>Reads feature flags, cached. Same trade-offs as <see cref="StoreSettingService"/>.</summary>
public class FeatureFlagService(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache) : IFeatureFlagService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey(name), out bool cached))
        {
            return cached;
        }

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFeatureFlagRepository>();

        var flag = await repository.GetByNameAsync(name, cancellationToken);

        // An unknown flag is off. A typo must not accidentally enable bKash.
        var enabled = flag?.IsEnabled ?? false;

        cache.Set(CacheKey(name), enabled, CacheDuration);

        return enabled;
    }

    public void Invalidate(string name) => cache.Remove(CacheKey(name));

    private static string CacheKey(string name) => $"flag:{name}";
}
