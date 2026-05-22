using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Vin = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    Series = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FuelLevelPercent = table.Column<double>(type: "double precision", nullable: true),
                    FuelRangeKm = table.Column<double>(type: "double precision", nullable: true),
                    OdometerKm = table.Column<double>(type: "double precision", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: true),
                    EngineRunning = table.Column<bool>(type: "boolean", nullable: true),
                    ClimateOn = table.Column<bool>(type: "boolean", nullable: true),
                    DriverDoorOpen = table.Column<bool>(type: "boolean", nullable: true),
                    PassengerDoorOpen = table.Column<bool>(type: "boolean", nullable: true),
                    RearLeftDoorOpen = table.Column<bool>(type: "boolean", nullable: true),
                    RearRightDoorOpen = table.Column<bool>(type: "boolean", nullable: true),
                    TrunkOpen = table.Column<bool>(type: "boolean", nullable: true),
                    BonnetOpen = table.Column<bool>(type: "boolean", nullable: true),
                    DriverWindowOpen = table.Column<bool>(type: "boolean", nullable: true),
                    PassengerWindowOpen = table.Column<bool>(type: "boolean", nullable: true),
                    RearLeftWindowOpen = table.Column<bool>(type: "boolean", nullable: true),
                    RearRightWindowOpen = table.Column<bool>(type: "boolean", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Speed = table.Column<double>(type: "double precision", nullable: true),
                    Heading = table.Column<double>(type: "double precision", nullable: true),
                    BatteryVoltage = table.Column<double>(type: "double precision", nullable: true),
                    InteriorTemperature = table.Column<double>(type: "double precision", nullable: true),
                    ExteriorTemperature = table.Column<double>(type: "double precision", nullable: true),
                    RawTopic = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetrySnapshots_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySnapshots_VehicleId_RecordedAt",
                table: "TelemetrySnapshots",
                columns: new[] { "VehicleId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Vin",
                table: "Vehicles",
                column: "Vin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetrySnapshots");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
