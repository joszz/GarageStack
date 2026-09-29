using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropRawTopicIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RawTopic",
                table: "TelemetrySnapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RawTopic",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RawTopic" },
                filter: "\"RawTopic\" IS NOT NULL");
        }
    }
}
