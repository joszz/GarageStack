using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripSpeedFigures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AvgMovingSpeedKmh",
                table: "Trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MaxSpeedKmh",
                table: "Trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovingSpeedSamples",
                table: "Trips",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvgMovingSpeedKmh",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "MaxSpeedKmh",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "MovingSpeedSamples",
                table: "Trips");
        }
    }
}
