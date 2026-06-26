using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPoiCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoiCacheTiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PoiType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CellLat = table.Column<int>(type: "integer", nullable: false),
                    CellLng = table.Column<int>(type: "integer", nullable: false),
                    CachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoiCacheTiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PoiItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PoiType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    MetaJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CellLat = table.Column<int>(type: "integer", nullable: false),
                    CellLng = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoiItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoiCacheTiles_ExpiresAt",
                table: "PoiCacheTiles",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PoiCacheTiles_Source_PoiType_CellLatLng",
                table: "PoiCacheTiles",
                columns: new[] { "Source", "PoiType", "CellLat", "CellLng" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoiItems_Source_ExternalId",
                table: "PoiItems",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoiItems_Source_PoiType_CellLatLng",
                table: "PoiItems",
                columns: new[] { "Source", "PoiType", "CellLat", "CellLng" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoiCacheTiles");

            migrationBuilder.DropTable(
                name: "PoiItems");
        }
    }
}
