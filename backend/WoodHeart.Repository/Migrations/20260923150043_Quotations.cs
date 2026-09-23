using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Quotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_lines_variant",
                table: "order_lines");

            migrationBuilder.AlterColumn<long>(
                name: "product_variant_id",
                table: "order_lines",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "product_id",
                table: "order_lines",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateTable(
                name: "quotations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    quotation_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    booking_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    customer_language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    ship_division = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ship_district = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ship_upazila = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ship_area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ship_address_line = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ship_landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ship_postcode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    delivery_zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    goods_net = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    prices_include_vat = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    delivery_fee_override = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    grand_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decline_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    converted_order_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotations", x => x.id);
                    table.ForeignKey(
                        name: "fk_quotations_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quotations_orders_converted_order_id",
                        column: x => x.converted_order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quotations_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quotation_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    quotation_id = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: true),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    image_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotation_lines", x => x.id);
                    table.CheckConstraint("ck_quotation_lines_catalogue", "(product_variant_id IS NULL AND product_id IS NULL) OR (product_variant_id IS NOT NULL AND product_id IS NOT NULL)");
                    table.CheckConstraint("ck_quotation_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_quotation_lines_product_variants_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quotation_lines_quotations_quotation_id",
                        column: x => x.quotation_id,
                        principalTable: "quotations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_variant",
                table: "order_lines",
                column: "product_variant_id",
                filter: "product_variant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_quotation_lines_product_variant_id",
                table: "quotation_lines",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotation_lines_quotation",
                table: "quotation_lines",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_booking",
                table: "quotations",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_contact_phone",
                table: "quotations",
                column: "contact_phone");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_customer_id",
                table: "quotations",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_status_valid_until",
                table: "quotations",
                columns: new[] { "status", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ux_quotations_converted_order",
                table: "quotations",
                column: "converted_order_id",
                unique: true,
                filter: "converted_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_quotations_number",
                table: "quotations",
                column: "quotation_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quotation_lines");

            migrationBuilder.DropTable(
                name: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_order_lines_variant",
                table: "order_lines");

            migrationBuilder.AlterColumn<long>(
                name: "product_variant_id",
                table: "order_lines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "product_id",
                table: "order_lines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_variant",
                table: "order_lines",
                column: "product_variant_id");
        }
    }
}
