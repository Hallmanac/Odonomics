using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddPostingHistorySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HistoryAccidentCount",
                table: "Postings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryFrameDamageStatement",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryPreviousOwnerCount",
                table: "Postings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryTitleWording",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryUseStatement",
                table: "Postings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HistoryAccidentCount",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "HistoryFrameDamageStatement",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "HistoryPreviousOwnerCount",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "HistoryTitleWording",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "HistoryUseStatement",
                table: "Postings");
        }
    }
}
