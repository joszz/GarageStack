using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDawarichPhoneFusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneFusion",
                table: "Trips",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneFusionAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PhoneBackfillRequests",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AfterTripId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneBackfillRequests", x => x.VehicleId);
                    table.ForeignKey(
                        name: "FK_PhoneBackfillRequests_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehiclePhoneTrackers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    TrackerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehiclePhoneTrackers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehiclePhoneTrackers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_PhoneFusion_EndedAt",
                table: "Trips",
                columns: new[] { "PhoneFusion", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePhoneTrackers_VehicleId_TrackerId",
                table: "VehiclePhoneTrackers",
                columns: new[] { "VehicleId", "TrackerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneBackfillRequests");

            migrationBuilder.DropTable(
                name: "VehiclePhoneTrackers");

            migrationBuilder.DropIndex(
                name: "IX_Trips_PhoneFusion_EndedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PhoneFusion",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PhoneFusionAt",
                table: "Trips");
        }
    }
}
