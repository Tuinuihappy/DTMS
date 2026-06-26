using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeItemUomToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: collapse legacy Uom variants into the canonical enum names
            // (KG / G / LB / EA / BOX / PALLET / CASE) so the new Quantity value object
            // can round-trip them. Lookup is case-insensitive and trim-tolerant.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'KG'
                    WHERE LOWER(TRIM(""Uom"")) IN ('kg','kgm','kilogram','กก');
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'G'
                    WHERE LOWER(TRIM(""Uom"")) IN ('g','grm','gram','กรัม');
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'LB'
                    WHERE LOWER(TRIM(""Uom"")) IN ('lb','lbs','pound');
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'EA'
                    WHERE LOWER(TRIM(""Uom"")) IN ('ea','each','pcs','pc','piece','ชิ้น');
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'BOX'
                    WHERE LOWER(TRIM(""Uom"")) IN ('box','ctn','carton','กล่อง');
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'PALLET'
                    WHERE LOWER(TRIM(""Uom"")) IN ('pallet','plt');
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'CASE'
                    WHERE LOWER(TRIM(""Uom"")) IN ('case','cs');

                -- Anything left that isn't one of the canonical seven becomes 'EA'.
                -- Pre-prod assumption: zero rows should hit this branch in real use;
                -- a dev/test row with junk like 'moo' or '' just normalizes safely.
                UPDATE deliveryorder.""Items"" SET ""Uom"" = 'EA'
                    WHERE ""Uom"" IS NULL
                       OR ""Uom"" NOT IN ('KG','G','LB','EA','BOX','PALLET','CASE');
            ");

            // No column-rename needed — EF OwnsOne for Quantity maps Value→Quantity
            // and Uom→Uom via HasColumnName, so the existing column shape is reused.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The Up step only mutates data values, not schema, and the original
            // strings were lost when we collapsed aliases. A meaningful rollback
            // is impossible — this Down is intentionally a no-op so that running
            // migrations downward leaves the database in a consistent state.
        }
    }
}
