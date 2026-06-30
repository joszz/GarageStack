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
            // UserRefreshTokens is created in AddAuthentication (20260522120000), which has a later
            // timestamp. On a fresh install migrations run in timestamp order, so this migration
            // runs first and the table does not exist yet. Use conditional SQL so that existing
            // deployments (where AddAuthentication already ran) get the indexes, while fresh
            // installs skip them safely -- RemoveAuthentication (20260526) drops the table anyway.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'public'
                        AND table_name = 'UserRefreshTokens'
                    ) THEN
                        CREATE INDEX IF NOT EXISTS "IX_UserRefreshTokens_Revoked_ExpiresAt"
                            ON "UserRefreshTokens" ("Revoked", "ExpiresAt");
                        CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserRefreshTokens_Token"
                            ON "UserRefreshTokens" ("Token");
                    END IF;
                END $$;
                """);

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
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_UserRefreshTokens_Revoked_ExpiresAt";
                DROP INDEX IF EXISTS "IX_UserRefreshTokens_Token";
                """);

            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt",
                table: "TelemetrySnapshots");

            migrationBuilder.DropIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RawTopic",
                table: "TelemetrySnapshots");
        }
    }
}
