using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Repository.Interfaces.Identity;

namespace WoodHeart.Repository.Repositories.Identity;

public class UserRefreshTokenRepository(DataContext context)
    : Repository<UserRefreshToken>(context), IUserRefreshTokenRepository
{
    public async Task<UserRefreshToken?> GetByHashAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<UserRefreshToken>> GetActiveForUserAsync(
        long userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await Set
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task RevokeChainAsync(
        long tokenId, DateTimeOffset now, string? byIp, CancellationToken cancellationToken = default)
    {
        var current = await Set.FirstOrDefaultAsync(x => x.Id == tokenId, cancellationToken);

        // Bounded rather than while(true): a cycle in ReplacedByTokenId should
        // be impossible, but an infinite loop inside a login request is a much
        // worse failure than giving up after a chain no session ever reaches.
        for (var hop = 0; current is not null && hop < 100; hop++)
        {
            if (current.RevokedAt is null)
            {
                current.RevokedAt = now;
                current.RevokedByIp = byIp;
            }

            if (current.ReplacedByTokenId is not { } next)
            {
                break;
            }

            current = await Set.FirstOrDefaultAsync(x => x.Id == next, cancellationToken);
        }
    }
}
