using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_Revoked_ExpiresAt",
                table: "UserRefreshTokens",
                columns: new[] { "Revoked", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_Token",
                table: "UserRefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "Latitude", "Longitude", "RecordedAt" },
                filter: "\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RawTopic",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RawTopic" },
                filter: "\"RawTopic\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_Revoked_ExpiresAt",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_Token",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RawTopic",
                table: "TelemetrySnapshots");
        }
    }
}
