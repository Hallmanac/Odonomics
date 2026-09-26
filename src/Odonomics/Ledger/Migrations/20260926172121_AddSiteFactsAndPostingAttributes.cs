using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteFactsAndPostingAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeePosture",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ItemizedFeesTotal",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PickupFee",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupLocation",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddOnsNote",
                table: "Dealers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DocFee",
                table: "Dealers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostingAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostingId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedRunId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostingAttributes_Postings_PostingId",
                        column: x => x.PostingId,
                        principalTable: "Postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostingAttributes_Runs_ObservedRunId",
                        column: x => x.ObservedRunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostingAttributes_ObservedRunId",
                table: "PostingAttributes",
                column: "ObservedRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAttributes_PostingId_Name",
                table: "PostingAttributes",
                columns: new[] { "PostingId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostingAttributes");

            migrationBuilder.DropColumn(
                name: "FeePosture",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "ItemizedFeesTotal",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "PickupFee",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "PickupLocation",
                table: "Postings");

            migrationBuilder.DropColumn(
                name: "AddOnsNote",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "DocFee",
                table: "Dealers");
        }
    }
}
