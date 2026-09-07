using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class OrdersAndNumberSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    next_value = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    shipping_division = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    shipping_district = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    shipping_upazila = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    shipping_area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    shipping_address_line = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    shipping_landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_postcode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    delivery_zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    goods_net = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    prices_include_vat = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    delivery_waived = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_overridden = table.Column<bool>(type: "boolean", nullable: false),
                    required_advance_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fulfilment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_method_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cart_id = table.Column<long>(type: "bigint", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_orders_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    product_name_bn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    product_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    delivery_charge_applied = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_lines_product_variants_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_timeline_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_timeline_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_timeline_entries_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_number_sequences_name_period",
                table: "number_sequences",
                columns: new[] { "name", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id",
                table: "order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_product",
                table: "order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_variant",
                table: "order_lines",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_timeline_order_occurred",
                table: "order_timeline_entries",
                columns: new[] { "order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_contact_phone",
                table: "orders",
                column: "contact_phone");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_placed",
                table: "orders",
                columns: new[] { "customer_id", "placed_at" },
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_placed",
                table: "orders",
                columns: new[] { "status", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_idempotency_key",
                table: "orders",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_orders_order_number",
                table: "orders",
                column: "order_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "order_lines");

            migrationBuilder.DropTable(
                name: "order_timeline_entries");

            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
