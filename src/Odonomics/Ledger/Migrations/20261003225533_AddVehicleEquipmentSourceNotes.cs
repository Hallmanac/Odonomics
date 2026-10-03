using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odonomics.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleEquipmentSourceNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeylessFobEntrySourceNote",
                table: "Vehicles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PushButtonStartSourceNote",
                table: "Vehicles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmartKeyEntrySourceNote",
                table: "Vehicles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeylessFobEntrySourceNote",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PushButtonStartSourceNote",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SmartKeyEntrySourceNote",
                table: "Vehicles");
        }
    }
}
