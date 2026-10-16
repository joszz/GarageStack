using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreChartIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddVehicleLastParkedAt dropped the statistics' partial index, so every database
            // created since lacks it; one older than that migration's edit still has it.
            migrationBuilder.Sql(TelemetryChartIndex.CreateIfMissingSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Left in place: a database that had the index before this migration keeps it.
        }
    }
}
