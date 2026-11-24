using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations;

/// <inheritdoc />
public partial class _20261124185218_AddTripJoinSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "JoinedFromJson",
            table: "Trips",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "JoinedFromJson",
            table: "Trips");
    }
}
