using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddRunSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sources",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sources",
                table: "Runs");
        }
    }
}
