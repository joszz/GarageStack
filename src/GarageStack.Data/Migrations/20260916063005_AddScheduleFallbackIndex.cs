using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleFallbackIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt_Schedule",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RecordedAt" },
                filter: "\"ChargingScheduleMode\" IS NOT NULL OR \"ChargingScheduleStartTime\" IS NOT NULL OR \"ChargingScheduleEndTime\" IS NOT NULL OR \"BatteryHeatingScheduleMode\" IS NOT NULL OR \"BatteryHeatingScheduleStartTime\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt_Schedule",
                table: "TelemetrySnapshots");
        }
    }
}
