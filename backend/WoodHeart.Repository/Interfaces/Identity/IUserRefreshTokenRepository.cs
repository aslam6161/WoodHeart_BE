using WoodHeart.Domain.Entity.Identity;

namespace WoodHeart.Repository.Interfaces.Identity;

public interface IUserRefreshTokenRepository : IRepository<UserRefreshToken>
{
    Task<UserRefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>All live tokens for a user — the "sign out everywhere" set.</summary>
    Task<IReadOnlyList<UserRefreshToken>> GetActiveForUserAsync(
        long userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a token and everything descended from it.
    /// </summary>
    /// <remarks>
    /// Called when an already-rotated token is presented again. That only
    /// happens if a token leaked, and the safe response is to kill the whole
    /// chain rather than guess which side of it is the attacker.
    /// </remarks>
    Task RevokeChainAsync(
        long tokenId, DateTimeOffset now, string? byIp, CancellationToken cancellationToken = default);
}
