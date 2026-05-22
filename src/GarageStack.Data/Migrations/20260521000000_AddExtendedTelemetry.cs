using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vehicle capability config
            migrationBuilder.AddColumn<string>(
                name: "ConfigJson",
                table: "Vehicles",
                type: "text",
                nullable: true);

            // HV drivetrain
            migrationBuilder.AddColumn<double>(
                name: "HvVoltage",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HvCurrent",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HvPower",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HvSocKwh",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HvTotalCapacityKwh",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PowerUsageSinceLastCharge",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChargerConnected",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HvBatteryActive",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            // Lights
            migrationBuilder.AddColumn<bool>(
                name: "LightsMainBeam",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LightsDippedBeam",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LightsSide",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            // Climate extras
            migrationBuilder.AddColumn<int>(
                name: "HeatedSeatFrontLeft",
                table: "TelemetrySnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeatedSeatFrontRight",
                table: "TelemetrySnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RearWindowDefroster",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConfigJson", table: "Vehicles");

            migrationBuilder.DropColumn(name: "HvVoltage", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "HvCurrent", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "HvPower", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "HvSocKwh", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "HvTotalCapacityKwh", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "PowerUsageSinceLastCharge", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "ChargerConnected", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "HvBatteryActive", table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(name: "LightsMainBeam", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "LightsDippedBeam", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "LightsSide", table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(name: "HeatedSeatFrontLeft", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "HeatedSeatFrontRight", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "RearWindowDefroster", table: "TelemetrySnapshots");
        }
    }
}
