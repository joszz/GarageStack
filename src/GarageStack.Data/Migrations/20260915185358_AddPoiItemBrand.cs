using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageStack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPoiItemBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "PoiItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // Backfill from the stored metadata so the map's brand filter keeps working for
            // rows cached before this column existed, instead of staying empty until every
            // tile has expired and been re-fetched. Row by row with a per-row exception guard:
            // MetaJson is always serializer output, but a single unparseable blob must not
            // abort the whole migration.
            migrationBuilder.Sql("""
                DO $$
                DECLARE r RECORD;
                BEGIN
                  FOR r IN SELECT "Id", "MetaJson" FROM "PoiItems" WHERE "MetaJson" IS NOT NULL LOOP
                    BEGIN
                      UPDATE "PoiItems"
                      SET "Brand" = LEFT(NULLIF(TRIM(COALESCE(r."MetaJson"::jsonb->>'brand', r."MetaJson"::jsonb->>'operator')), ''), 128)
                      WHERE "Id" = r."Id";
                    EXCEPTION WHEN invalid_text_representation THEN
                      NULL;
                    END;
                  END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PoiItems_Source_PoiType_Brand",
                table: "PoiItems",
                columns: new[] { "Source", "PoiType", "Brand" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PoiItems_Source_PoiType_Brand",
                table: "PoiItems");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "PoiItems");
        }
    }
}
