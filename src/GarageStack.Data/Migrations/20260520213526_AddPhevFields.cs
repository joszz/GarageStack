using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhevFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EvSocPercent",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCharging",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SunRoofOpen",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TyrePressureFrontLeft",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TyrePressureFrontRight",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TyrePressureRearLeft",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TyrePressureRearRight",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvSocPercent",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "IsCharging",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "SunRoofOpen",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "TyrePressureFrontLeft",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "TyrePressureFrontRight",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "TyrePressureRearLeft",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "TyrePressureRearRight",
                table: "TelemetrySnapshots");
        }
    }
}
