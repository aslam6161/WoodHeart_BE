using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Repository;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// What a transaction does with a use case that reports failure.
/// </summary>
/// <remarks>
/// Found by the stock ledger's end-to-end run: an order refused for stock
/// answered 409 and was in the database anyway. The placement saves the
/// order, then reserves; the refusal returned a failure, and the wrapper
/// committed whatever had been saved because nothing had thrown. The same
/// hole was under the payment-provider refusal since Phase 2 — cash on
/// delivery never declines, so nobody had fallen through it.
/// </remarks>
public class UnitOfWorkDatabaseTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [RequiresPostgresFact]
    public async Task A_failed_result_rolls_back_what_was_saved_inside_the_transaction()
    {
        var key = $"test.{Guid.NewGuid():n}";

        await using (var context = fixture.CreateContext())
        {
            var result = await context.ExecuteInTransactionAsync(async ct =>
            {
                context.StoreSettings.Add(Setting(key));
                await context.SaveChangesAsync(ct);

                // Saved, then refused — the shape of a placement that finds
                // the shelf empty after the order row exists.
                return GeneralResponse.Fail("test.refused", "No.");
            });

            result.IsSuccess.ShouldBeFalse();
        }

        await using var reading = fixture.CreateContext();

        (await reading.StoreSettings.AnyAsync(s => s.Key == key)).ShouldBeFalse();
    }

    [RequiresPostgresFact]
    public async Task A_successful_result_commits()
    {
        var key = $"test.{Guid.NewGuid():n}";

        await using (var context = fixture.CreateContext())
        {
            await context.ExecuteInTransactionAsync(async ct =>
            {
                context.StoreSettings.Add(Setting(key));
                await context.SaveChangesAsync(ct);

                return GeneralResponse.Success();
            });
        }

        await using var reading = fixture.CreateContext();

        (await reading.StoreSettings.AnyAsync(s => s.Key == key)).ShouldBeTrue();
    }

    [RequiresPostgresFact]
    public async Task A_result_that_is_not_a_response_commits_as_before()
    {
        var key = $"test.{Guid.NewGuid():n}";

        await using (var context = fixture.CreateContext())
        {
            var count = await context.ExecuteInTransactionAsync(async ct =>
            {
                context.StoreSettings.Add(Setting(key));
                return await context.SaveChangesAsync(ct);
            });

            count.ShouldBe(1);
        }

        await using var reading = fixture.CreateContext();

        (await reading.StoreSettings.AnyAsync(s => s.Key == key)).ShouldBeTrue();
    }

    private static StoreSetting Setting(string key) =>
        new() { Key = key, Value = "1", ValueType = SettingValueType.String, Category = "Test" };
}
