using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChartIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Partial index covering only chart-relevant rows; the general
            // IX_TelemetrySnapshots_VehicleId_RecordedAt index is kept for
            // GetLatestAsync / GetMergedLatestAsync which have no field filter.
            migrationBuilder.Sql("""
                CREATE INDEX "IX_TelemetrySnapshots_VehicleId_RecordedAt_Chart"
                    ON "TelemetrySnapshots" ("VehicleId", "RecordedAt")
                    WHERE "FuelLevelPercent"        IS NOT NULL
                       OR "EvSocPercent"            IS NOT NULL
                       OR "PowerUsageOfDay"         IS NOT NULL
                       OR "BatteryVoltage"          IS NOT NULL
                       OR "ClimateOn"               IS NOT NULL
                       OR "IsCharging"              IS NOT NULL
                       OR "TyrePressureFrontLeft"   IS NOT NULL
                       OR "TyrePressureFrontRight"  IS NOT NULL
                       OR "TyrePressureRearLeft"    IS NOT NULL
                       OR "TyrePressureRearRight"   IS NOT NULL
                       OR "MileageOfTheDay"         IS NOT NULL
                       OR "MileageSinceLastCharge"  IS NOT NULL
                       OR "HvSocKwh"               IS NOT NULL
                       OR "HvTotalCapacityKwh"      IS NOT NULL
                       OR "PowerUsageSinceLastCharge" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_TelemetrySnapshots_VehicleId_RecordedAt_Chart";
                """);
        }
    }
}
