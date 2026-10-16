using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParkedPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ParkedLatitude",
                table: "Vehicles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ParkedLongitude",
                table: "Vehicles",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParkedLatitude",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ParkedLongitude",
                table: "Vehicles");
        }
    }
}
