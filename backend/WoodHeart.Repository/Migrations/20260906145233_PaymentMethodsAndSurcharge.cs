using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class PaymentMethodsAndSurcharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "payment_surcharge",
                table: "orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payment_method_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "jsonb", nullable: true),
                    icon_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    min_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    max_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    available_inside_dhaka = table.Column<bool>(type: "boolean", nullable: false),
                    available_outside_dhaka = table.Column<bool>(type: "boolean", nullable: false),
                    charge_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    charge_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    credentials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_method_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payment_method_configs_code",
                table: "payment_method_configs",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_method_configs");

            migrationBuilder.DropColumn(
                name: "payment_surcharge",
                table: "orders");
        }
    }
}
