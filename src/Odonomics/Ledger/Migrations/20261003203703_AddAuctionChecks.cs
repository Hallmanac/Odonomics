using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionChecks",
                columns: table => new
                {
                    Vin = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CouldNotReadReason = table.Column<string>(type: "TEXT", nullable: true),
                    Auction = table.Column<string>(type: "TEXT", nullable: true),
                    LotNumber = table.Column<string>(type: "TEXT", nullable: true),
                    SaleDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SaleDocument = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryDamage = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryDamage = table.Column<string>(type: "TEXT", nullable: true),
                    Acv = table.Column<decimal>(type: "TEXT", nullable: true),
                    RepairEstimate = table.Column<decimal>(type: "TEXT", nullable: true),
                    Odometer = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionChecks", x => x.Vin);
                    table.ForeignKey(
                        name: "FK_AuctionChecks_Vehicles_Vin",
                        column: x => x.Vin,
                        principalTable: "Vehicles",
                        principalColumn: "Vin",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionChecks");
        }
    }
}
