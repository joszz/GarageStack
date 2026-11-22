using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageStack.Data.Migrations;

/// <inheritdoc />
public partial class _20261122193356_AddPhonePositions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PhonePositions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                VehicleId = table.Column<int>(type: "integer", nullable: false),
                TrackerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Latitude = table.Column<double>(type: "double precision", nullable: false),
                Longitude = table.Column<double>(type: "double precision", nullable: false),
                AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                SpeedKmh = table.Column<double>(type: "double precision", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhonePositions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PhonePositions_Vehicles_VehicleId",
                    column: x => x.VehicleId,
                    principalTable: "Vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PhonePositions_VehicleId_RecordedAt",
            table: "PhonePositions",
            columns: new[] { "VehicleId", "RecordedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_PhonePositions_VehicleId_TrackerId_RecordedAt",
            table: "PhonePositions",
            columns: new[] { "VehicleId", "TrackerId", "RecordedAt" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PhonePositions");
    }
}
