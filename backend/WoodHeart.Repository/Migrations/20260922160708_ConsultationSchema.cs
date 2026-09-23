using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ConsultationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consultants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    photo_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    bio = table.Column<string>(type: "jsonb", nullable: true),
                    specialities = table.Column<string[]>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consultation_services",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "jsonb", nullable: true),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    requires_advance = table.Column<bool>(type: "boolean", nullable: false),
                    advance_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    buffer_before_minutes = table.Column<int>(type: "integer", nullable: false),
                    buffer_after_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultation_services", x => x.id);
                    table.CheckConstraint("ck_consultation_services_duration", "duration_minutes > 0");
                });

            migrationBuilder.CreateTable(
                name: "availability_exceptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    consultant_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_availability_exceptions_consultants_consultant_id",
                        column: x => x.consultant_id,
                        principalTable: "consultants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "availability_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    consultant_id = table.Column<long>(type: "bigint", nullable: false),
                    day_of_week = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    slot_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_rules", x => x.id);
                    table.CheckConstraint("ck_availability_rules_slot", "slot_minutes > 0");
                    table.CheckConstraint("ck_availability_rules_window", "end_time > start_time");
                    table.ForeignKey(
                        name: "fk_availability_rules_consultants_consultant_id",
                        column: x => x.consultant_id,
                        principalTable: "consultants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    booking_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    consultation_service_id = table.Column<long>(type: "bigint", nullable: false),
                    consultant_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    customer_language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    scheduled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    previous_scheduled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    site_division = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    site_district = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    site_upazila = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    site_area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    site_address_line = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    site_landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    site_postcode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    project_brief = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    budget_range = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    room_types = table.Column<string[]>(type: "text[]", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    advance_due = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.ForeignKey(
                        name: "fk_bookings_consultants_consultant_id",
                        column: x => x.consultant_id,
                        principalTable: "consultants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_consultation_services_consultation_service_id",
                        column: x => x.consultation_service_id,
                        principalTable: "consultation_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consultant_services",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    consultant_id = table.Column<long>(type: "bigint", nullable: false),
                    consultation_service_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultant_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_consultant_services_consultants_consultant_id",
                        column: x => x.consultant_id,
                        principalTable: "consultants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_consultant_services_consultation_services_consultation_serv",
                        column: x => x.consultation_service_id,
                        principalTable: "consultation_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_timeline_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    booking_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_booking_timeline_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_timeline_entries_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_availability_exceptions_consultant_date",
                table: "availability_exceptions",
                columns: new[] { "consultant_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_availability_rules_consultant_day",
                table: "availability_rules",
                columns: new[] { "consultant_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_timeline_booking_occurred",
                table: "booking_timeline_entries",
                columns: new[] { "booking_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_consultation_service_id",
                table: "bookings",
                column: "consultation_service_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_contact_phone",
                table: "bookings",
                column: "contact_phone");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_id",
                table: "bookings",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_scheduled_status",
                table: "bookings",
                columns: new[] { "scheduled_at_utc", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_bookings_consultant_slot",
                table: "bookings",
                columns: new[] { "consultant_id", "scheduled_at_utc" },
                unique: true,
                filter: "consultant_id IS NOT NULL AND status IN ('Requested', 'Confirmed', 'Rescheduled')");

            migrationBuilder.CreateIndex(
                name: "ux_bookings_idempotency_key",
                table: "bookings",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_bookings_number",
                table: "bookings",
                column: "booking_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consultant_services_consultation_service_id",
                table: "consultant_services",
                column: "consultation_service_id");

            migrationBuilder.CreateIndex(
                name: "ux_consultant_services_pair",
                table: "consultant_services",
                columns: new[] { "consultant_id", "consultation_service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_consultation_services_slug",
                table: "consultation_services",
                column: "slug",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_exceptions");

            migrationBuilder.DropTable(
                name: "availability_rules");

            migrationBuilder.DropTable(
                name: "booking_timeline_entries");

            migrationBuilder.DropTable(
                name: "consultant_services");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "consultants");

            migrationBuilder.DropTable(
                name: "consultation_services");
        }
    }
}
