using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations;

/// <inheritdoc />
public partial class _20261123072920_PowerTailgateFromCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<bool>(
            name: "HasPowerTailgate",
            table: "Vehicles",
            type: "boolean",
            nullable: true,
            oldClrType: typeof(bool),
            oldType: "boolean");

        // Off was the default, not a choice anyone can be told from it: those cars now follow
        // their codes. A car switched on stays on.
        migrationBuilder.Sql("""UPDATE "Vehicles" SET "HasPowerTailgate" = NULL WHERE "HasPowerTailgate" = FALSE;""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""UPDATE "Vehicles" SET "HasPowerTailgate" = FALSE WHERE "HasPowerTailgate" IS NULL;""");

        migrationBuilder.AlterColumn<bool>(
            name: "HasPowerTailgate",
            table: "Vehicles",
            type: "boolean",
            nullable: false,
            defaultValue: false,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldNullable: true);
    }
}
