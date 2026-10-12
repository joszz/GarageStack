using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommutePlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PurposeIsManual",
                table: "Trips",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every purpose on record so far was chosen by someone: automatic labels start here.
            // Without this, the first home and work set would re-label trips the driver classified.
            migrationBuilder.Sql("""UPDATE "Trips" SET "PurposeIsManual" = true WHERE "Purpose" IS NOT NULL""");

            migrationBuilder.CreateTable(
                name: "CommutePlaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    RadiusMeters = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommutePlaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommutePlaces_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommutePlaces_VehicleId",
                table: "CommutePlaces",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommutePlaces");

            migrationBuilder.DropColumn(
                name: "PurposeIsManual",
                table: "Trips");
        }
    }
}
