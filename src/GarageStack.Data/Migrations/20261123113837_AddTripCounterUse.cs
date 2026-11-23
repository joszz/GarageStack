using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations;

/// <inheritdoc />
public partial class _20261123113837_AddTripCounterUse : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CounterReadings",
            table: "Trips",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "CounterUsed",
            table: "Trips",
            type: "double precision",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CounterReadings",
            table: "Trips");

        migrationBuilder.DropColumn(
            name: "CounterUsed",
            table: "Trips");
    }
}
