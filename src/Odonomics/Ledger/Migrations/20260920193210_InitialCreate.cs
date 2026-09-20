using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Vin = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Make = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Trim = table.Column<string>(type: "TEXT", nullable: true),
                    Mileage = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeen = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinalistMarkedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Vin);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VehicleVin = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notes_Vehicles_VehicleVin",
                        column: x => x.VehicleVin,
                        principalTable: "Vehicles",
                        principalColumn: "Vin",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Postings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VehicleVin = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeen = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Postings_Vehicles_VehicleVin",
                        column: x => x.VehicleVin,
                        principalTable: "Vehicles",
                        principalColumn: "Vin",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VinRecords",
                columns: table => new
                {
                    Vin = table.Column<string>(type: "TEXT", nullable: false),
                    DecodedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DecodeRawJson = table.Column<string>(type: "TEXT", nullable: false),
                    OpenRecallCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RecallsRawJson = table.Column<string>(type: "TEXT", nullable: true),
                    ComplaintCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinRecords", x => x.Vin);
                    table.ForeignKey(
                        name: "FK_VinRecords_Vehicles_Vin",
                        column: x => x.Vin,
                        principalTable: "Vehicles",
                        principalColumn: "Vin",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostingId = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceObservations_Postings_PostingId",
                        column: x => x.PostingId,
                        principalTable: "Postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_VehicleVin",
                table: "Notes",
                column: "VehicleVin");

            migrationBuilder.CreateIndex(
                name: "IX_Postings_VehicleVin_Source_Url",
                table: "Postings",
                columns: new[] { "VehicleVin", "Source", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceObservations_PostingId",
                table: "PriceObservations",
                column: "PostingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "PriceObservations");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "VinRecords");

            migrationBuilder.DropTable(
                name: "Postings");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
