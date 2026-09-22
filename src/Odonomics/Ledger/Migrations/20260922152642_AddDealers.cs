using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddDealers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DealerId",
                table: "Postings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Dealers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedLocation = table.Column<string>(type: "TEXT", nullable: false),
                    Grade = table.Column<string>(type: "TEXT", nullable: true),
                    GradeReason = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dealers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Postings_DealerId",
                table: "Postings",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_Dealers_NormalizedName_NormalizedLocation",
                table: "Dealers",
                columns: new[] { "NormalizedName", "NormalizedLocation" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Postings_Dealers_DealerId",
                table: "Postings",
                column: "DealerId",
                principalTable: "Dealers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Postings_Dealers_DealerId",
                table: "Postings");

            migrationBuilder.DropTable(
                name: "Dealers");

            migrationBuilder.DropIndex(
                name: "IX_Postings_DealerId",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "DealerId",
                table: "Postings");
        }
    }
}
