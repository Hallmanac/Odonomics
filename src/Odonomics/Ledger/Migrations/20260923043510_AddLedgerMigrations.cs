using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerMigrations",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerMigrations", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerMigrations");
        }
    }
}
