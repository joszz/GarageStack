using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleLastParkedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt_Chart",
                table: "TelemetrySnapshots");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastParkedAt",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropColumn(
                name: "LastParkedAt",
                table: "Vehicles");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt_Chart",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RecordedAt" },
                filter: "\"FuelLevelPercent\" IS NOT NULL OR \"EvSocPercent\" IS NOT NULL OR \"PowerUsageOfDay\" IS NOT NULL OR \"BatteryVoltage\" IS NOT NULL OR \"ClimateOn\" IS NOT NULL OR \"IsCharging\" IS NOT NULL OR \"TyrePressureFrontLeft\" IS NOT NULL OR \"TyrePressureFrontRight\" IS NOT NULL OR \"TyrePressureRearLeft\" IS NOT NULL OR \"TyrePressureRearRight\" IS NOT NULL OR \"MileageOfTheDay\" IS NOT NULL OR \"MileageSinceLastCharge\" IS NOT NULL OR \"HvSocKwh\" IS NOT NULL OR \"HvTotalCapacityKwh\" IS NOT NULL OR \"PowerUsageSinceLastCharge\" IS NOT NULL");
        }
    }
}
