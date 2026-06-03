using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeSessionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BmsChargeStatus",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChargingLastEndAt",
                table: "TelemetrySnapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargingScheduleEndTime",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargingScheduleMode",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargingScheduleStartTime",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LastChargeEndingPower",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OffboardChargerPlugStatus",
                table: "TelemetrySnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OnboardChargerPlugStatus",
                table: "TelemetrySnapshots",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BmsChargeStatus",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingLastEndAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingScheduleEndTime",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingScheduleMode",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingScheduleStartTime",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "LastChargeEndingPower",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "OffboardChargerPlugStatus",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "OnboardChargerPlugStatus",
                table: "TelemetrySnapshots");
        }
    }
}
