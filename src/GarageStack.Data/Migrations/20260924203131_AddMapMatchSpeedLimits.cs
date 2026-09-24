using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMapMatchSpeedLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpeedLimitRunsJson",
                table: "MapMatchCacheEntries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeedLimitRunsJson",
                table: "MapMatchCacheEntries");
        }
    }
}
