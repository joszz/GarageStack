using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations;

/// <inheritdoc />
public partial class _20261126205019_AddPushSubscriptionVehicleExclusions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExcludedCategoriesByVehicle",
            table: "PushSubscriptions",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'{}'::jsonb");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ExcludedCategoriesByVehicle",
            table: "PushSubscriptions");
    }
}
