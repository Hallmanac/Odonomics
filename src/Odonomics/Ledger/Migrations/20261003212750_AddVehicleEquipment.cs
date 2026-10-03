using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PushButtonStart",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "PushButtonStartSource",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "SmartKeyEntry",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "SmartKeyEntrySource",
                table: "Vehicles",
                type: "TEXT",
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PushButtonStart",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PushButtonStartSource",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SmartKeyEntry",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SmartKeyEntrySource",
                table: "Vehicles");
        }
    }
}
