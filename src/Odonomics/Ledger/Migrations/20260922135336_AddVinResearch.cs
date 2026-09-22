using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddVinResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentListingDaysOnMarket",
                table: "VinRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryRawJson",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResearchedAt",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyFrontRating",
                table: "VinRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyOverallRating",
                table: "VinRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyRawJson",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyRolloverRating",
                table: "VinRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetySideRating",
                table: "VinRecords",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentListingDaysOnMarket",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "HistoryRawJson",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "ResearchedAt",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetyFrontRating",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetyOverallRating",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetyRawJson",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetyRolloverRating",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "SafetySideRating",
                table: "VinRecords");
        }
    }
}
