using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <summary>
    /// Removes two feature flags that nothing ever read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seeder only inserts flags it finds missing, so removing them from
    /// the code leaves the rows behind on every database that already has them
    /// — and a row in <c>feature_flags</c> reads as a switch somebody can
    /// throw. These two were not.
    /// </para>
    /// <para>
    /// <c>bkash.enabled</c> duplicated <c>payment_method_configs.is_enabled</c>,
    /// which is the real switch, sits on a screen the shop already uses, and is
    /// backed by a resolver that refuses any method with no provider registered.
    /// <c>reviews.enabled</c> was seeded for a feature that does not exist yet
    /// and will bring its own switch when it arrives.
    /// </para>
    /// </remarks>
    public partial class RemoveUnreadFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM feature_flags WHERE name IN ('bkash.enabled', 'reviews.enabled');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Put back exactly as the seeder had them, so rolling back leaves
            // the database as it was rather than merely un-deleted.
            migrationBuilder.Sql(
                """
                INSERT INTO feature_flags (name, is_enabled, description, created_at)
                VALUES
                    ('bkash.enabled', false, 'Offer bKash at checkout.', now()),
                    ('reviews.enabled', false, 'Allow customers to review products.', now())
                ON CONFLICT DO NOTHING;
                """);
        }
    }
}
