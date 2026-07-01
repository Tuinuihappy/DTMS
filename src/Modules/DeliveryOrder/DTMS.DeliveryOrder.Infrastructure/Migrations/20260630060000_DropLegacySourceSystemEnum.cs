using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P6 — final cleanup of the SourceSystem enum → dynamic-key
    /// migration. Drops the legacy varchar(20) enum column, its unique
    /// index, and promotes <c>SourceSystemKey</c> / <c>SourceSystemDisplayName</c>
    /// to NOT NULL.
    ///
    /// <para><b>Pre-conditions</b>: P1-P5 all applied.
    /// After P4 the write path stamps both new columns on every insert;
    /// after P2 backfill every historical row has values; after P5 the
    /// projections read from the new columns. Nothing else in the code
    /// touches <c>SourceSystem</c>-the-enum-column.</para>
    ///
    /// <para><b>Rollback</b>: Down re-creates the varchar(20) column,
    /// backfills from <c>SourceSystemKey</c> (title-cased for the four
    /// well-known slugs; empty string for anything new), and re-adds the
    /// legacy unique index. Any orders inserted with a truly-new dynamic
    /// slug that has no enum equivalent get an empty enum column — the
    /// legacy index enforces uniqueness on <c>(''::text, OrderRef)</c>
    /// so bulk-cancel of those rows is possible before restoring the app
    /// to P5.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260630060000_DropLegacySourceSystemEnum")]
    public partial class DropLegacySourceSystemEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the legacy unique index. It was co-located with the
            //    new IX_DeliveryOrders_SourceSystemKey_OrderRef throughout
            //    P2-P5 (dual-window); we've reached the tail of the window.
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_SourceSystem_OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            // 2. Drop the legacy enum column.
            migrationBuilder.DropColumn(
                name: "SourceSystem",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            // 3. Promote SourceSystemKey to NOT NULL. Every row is backfilled
            //    (P2) and every new write stamps a value (P4), so this can
            //    execute without a default fallback.
            migrationBuilder.AlterColumn<string>(
                name: "SourceSystemKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            // 4. Promote SourceSystemDisplayName to NOT NULL for the same
            //    reason. Any legacy row that couldn't resolve a DisplayName
            //    from iam.SystemClients (JOIN miss during P2 backfill) gets
            //    an empty string here — the CHECK below would fire if any
            //    row is still NULL, giving a clear error message.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                   SET ""SourceSystemDisplayName"" = ''
                 WHERE ""SourceSystemDisplayName"" IS NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "SourceSystemDisplayName",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            // 5. Consistency — widen bi.OrderFacts.SourceSystem from
            //    varchar(20) (sized for enum names) to varchar(50) to
            //    match the OrderListView + DeliveryOrders slug width.
            //    Existing rows are all short so no data change needed.
            migrationBuilder.AlterColumn<string>(
                name: "SourceSystem",
                schema: "bi",
                table: "OrderFacts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse in strict opposite order — otherwise the legacy
            // unique index cannot be recreated (would collide with new
            // NULL columns, or worse, break FK checks).

            migrationBuilder.AlterColumn<string>(
                name: "SourceSystem",
                schema: "bi",
                table: "OrderFacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "SourceSystemDisplayName",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SourceSystemKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Backfill the legacy enum column from the still-populated key.
            // Case-map back to PascalCase for the four well-known slugs.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                   SET ""SourceSystem"" = CASE ""SourceSystemKey""
                        WHEN 'manual' THEN 'Manual'
                        WHEN 'oms'    THEN 'Oms'
                        WHEN 'sap'    THEN 'Sap'
                        WHEN 'erp'    THEN 'Erp'
                        ELSE ''
                    END;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_SourceSystem_OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                columns: new[] { "SourceSystem", "OrderRef" },
                unique: true);
        }
    }
}
