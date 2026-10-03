using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleKeylessFobEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeylessFobEntry",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "KeylessFobEntrySource",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                defaultValue: "None");

            // Before this migration a sticker that listed only a fob "Keyless Entry" stored push-button start as
            // absent, which says nothing about how the car starts. A stored absence cannot be told apart from a
            // real turn-key one, so every stored absence goes back to unknown; a walk with --revisit reads the
            // sticker again and stores what it now supports.
            migrationBuilder.Sql(
                "UPDATE Vehicles SET PushButtonStart = 'Unknown', PushButtonStartSource = 'None' WHERE PushButtonStart = 'Absent'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeylessFobEntry",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KeylessFobEntrySource",
                table: "Vehicles");
        }
    }
}
