using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEfficiencyAndPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SaicUser",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MileageOfTheDay",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MileageSinceLastCharge",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PowerUsageOfDay",
                table: "TelemetrySnapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    P256DhKey = table.Column<string>(type: "text", nullable: false),
                    AuthKey = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PushSubscriptions");

            migrationBuilder.DropColumn(name: "MileageOfTheDay", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "MileageSinceLastCharge", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "PowerUsageOfDay", table: "TelemetrySnapshots");
            migrationBuilder.DropColumn(name: "SaicUser", table: "Vehicles");
        }
    }
}
