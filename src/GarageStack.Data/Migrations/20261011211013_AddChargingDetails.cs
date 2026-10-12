using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChargingDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChargeCurrentLimit",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChargingLastStartAt",
                table: "TelemetrySnapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargingStopReason",
                table: "TelemetrySnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SocTargetPercent",
                table: "TelemetrySnapshots",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargeCurrentLimit",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingLastStartAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "ChargingStopReason",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "SocTargetPercent",
                table: "TelemetrySnapshots");
        }
    }
}
