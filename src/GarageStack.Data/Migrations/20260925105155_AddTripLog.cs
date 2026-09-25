using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every trip saved so far is derived from telemetry alone: nobody has recorded a purpose
            // or a note yet, because this migration is what makes that possible. Rather than fill
            // the new columns in SQL, which would repeat the Worker's rules, the trips are cleared
            // and the recording line reset, and the Worker saves them again on its next run with
            // their ends and odometer readings.
            migrationBuilder.Sql("""DELETE FROM "Trips";""");
            migrationBuilder.Sql("""UPDATE "Vehicles" SET "TripsRecordedUntil" = NULL;""");

            migrationBuilder.AddColumn<double>(
                name: "EndLatitude",
                table: "Trips",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "EndLongitude",
                table: "Trips",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "EndPlaceJson",
                table: "Trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Trips",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OdometerEndKm",
                table: "Trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OdometerStartKm",
                table: "Trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Trips",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StartLatitude",
                table: "Trips",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "StartLongitude",
                table: "Trips",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "StartPlaceJson",
                table: "Trips",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndLatitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "EndLongitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "EndPlaceJson",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "OdometerEndKm",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "OdometerStartKm",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartLatitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartLongitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartPlaceJson",
                table: "Trips");
        }
    }
}
