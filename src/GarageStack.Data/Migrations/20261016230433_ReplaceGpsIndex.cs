using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceGpsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt",
                table: "TelemetrySnapshots");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt_Gps",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RecordedAt" },
                filter: "\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "Latitude", "Longitude", "Speed", "Heading" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt_Gps",
                table: "TelemetrySnapshots");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "Latitude", "Longitude", "RecordedAt" },
                filter: "\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL");
        }
    }
}
