using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleSettings",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    HvBatteryCapacityKwh = table.Column<double>(type: "double precision", nullable: true),
                    TyreLowBar = table.Column<double>(type: "double precision", nullable: true),
                    TyreGoodBar = table.Column<double>(type: "double precision", nullable: true),
                    TyreHighBar = table.Column<double>(type: "double precision", nullable: true),
                    TyreDropBar = table.Column<double>(type: "double precision", nullable: true),
                    AuxBatteryLowVolt = table.Column<double>(type: "double precision", nullable: true),
                    LowFuelPercent = table.Column<double>(type: "double precision", nullable: true),
                    LowEvPercent = table.Column<double>(type: "double precision", nullable: true),
                    VehicleType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleSettings", x => x.VehicleId);
                    table.ForeignKey(
                        name: "FK_VehicleSettings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleSettings");
        }
    }
}
