using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddNhtsaPieceFetchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComplaintsCouldNotFetchReason",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ComplaintsFetchedAt",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecallsCouldNotFetchReason",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecallsFetchedAt",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyCouldNotFetchReason",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SafetyFetchedAt",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplaintsCouldNotFetchReason",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "ComplaintsFetchedAt",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "RecallsCouldNotFetchReason",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "RecallsFetchedAt",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetyCouldNotFetchReason",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetyFetchedAt",
                table: "VinRecords");
        }
    }
}
