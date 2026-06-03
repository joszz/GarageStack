using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedTelemetryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BatteryHeating",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatteryHeatingScheduleMode",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatteryHeatingScheduleStartTime",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChargingCableLock",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargingType",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentJourneyDistance",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Elevation",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "TelemetrySnapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChargeStateAt",
                table: "TelemetrySnapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVehicleStateAt",
                table: "TelemetrySnapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ObcCurrent",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ObcPowerSinglePhase",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ObcPowerThreePhase",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ObcVoltage",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemainingChargingTime",
                table: "TelemetrySnapshots",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatteryHeating",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "BatteryHeatingScheduleMode",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "BatteryHeatingScheduleStartTime",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingCableLock",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingType",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "CurrentJourneyDistance",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "Elevation",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "LastChargeStateAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "LastVehicleStateAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ObcCurrent",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ObcPowerSinglePhase",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ObcPowerThreePhase",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ObcVoltage",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "RemainingChargingTime",
                table: "TelemetrySnapshots");
        }
    }
}
