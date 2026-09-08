using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class OrderCustomerLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "en" rather than the scaffolder's empty string. Orders placed
            // before this column existed were confirmed in English, and a blank
            // language would make the templates fall through to their default
            // by accident rather than by decision.
            migrationBuilder.AddColumn<string>(
                name: "customer_language",
                table: "orders",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customer_language",
                table: "orders");
        }
    }
}
