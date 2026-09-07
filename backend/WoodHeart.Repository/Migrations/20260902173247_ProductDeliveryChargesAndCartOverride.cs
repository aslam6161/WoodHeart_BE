using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ProductDeliveryChargesAndCartOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old `delivery_surcharge` meant "extra carriage on top of the
            // flat zone rate". Delivery is now priced per product per zone, so
            // the nearest surviving meaning is the inside-Dhaka charge — EF
            // guessed the outside column, which would have left inside-Dhaka
            // blank, and blank now falls back to the store default.
            migrationBuilder.RenameColumn(
                name: "delivery_surcharge",
                table: "products",
                newName: "delivery_charge_inside_dhaka");

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_charge_outside_dhaka",
                table: "products",
                type: "numeric(18,2)",
                nullable: true);

            // Seed the outside charge from the inside one wherever a product
            // has been costed at all.
            //
            // It is certainly too low — sending a sofa to Sylhet costs more
            // than sending it across Dhanmondi — but it is a floor rather than
            // a hole. Leaving it null would drop those products to the store
            // default, which is currently zero, and the shop would carry its
            // bulkiest goods across the country for nothing. Someone should
            // review these figures; a low charge is visible, a free one is not.
            migrationBuilder.Sql(
                """
                UPDATE products
                SET delivery_charge_outside_dhaka = delivery_charge_inside_dhaka
                WHERE delivery_charge_inside_dhaka IS NOT NULL;
                """);

            // The VAT rate now has a real answer: 7.5%.
            //
            // The seeder only inserts settings that are missing, so a database
            // that already holds the placeholder would keep charging zero. This
            // updates it — but only where it is still the untouched placeholder.
            // Anything else is a figure a person chose, and a deployment must
            // not overwrite that.
            migrationBuilder.Sql(
                """
                UPDATE store_settings
                SET value = '7.5'
                WHERE key = 'tax.vat_rate' AND value IN ('0', '0.0', '0.00');
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_fee_override",
                table: "carts",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_fee_override_note",
                table: "carts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_charge_outside_dhaka",
                table: "products");

            migrationBuilder.DropColumn(
                name: "delivery_fee_override",
                table: "carts");

            migrationBuilder.DropColumn(
                name: "delivery_fee_override_note",
                table: "carts");

            migrationBuilder.RenameColumn(
                name: "delivery_charge_inside_dhaka",
                table: "products",
                newName: "delivery_surcharge");

            migrationBuilder.Sql(
                """
                UPDATE store_settings SET value = '0' WHERE key = 'tax.vat_rate' AND value = '7.5';
                """);
        }
    }
}
