using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryFetchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HistoryCouldNotFetchReason",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoryFetchedAt",
                table: "VinRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HistoryCouldNotFetchReason",
                table: "VinRecords");

            migrationBuilder.DropColumn(
                name: "HistoryFetchedAt",
                table: "VinRecords");
        }
    }
}
