using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <inheritdoc />
    public partial class BookingReminderStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "final_reminder_sent_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_reminder_sent_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "final_reminder_sent_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "first_reminder_sent_at",
                table: "bookings");
        }
    }
}
