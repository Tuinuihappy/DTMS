using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P5 of SourceSystem enum→dynamic-key migration. Three moves:
    /// <list type="number">
    ///   <item>Widen <c>OrderListView.SourceSystem</c> from varchar(20)
    ///   to varchar(50) so dynamic slugs like <c>wms-acme</c> fit.</item>
    ///   <item>Add <c>OrderListView.SourceSystemDisplayName</c> column +
    ///   backfill from <c>iam.SystemClients</c> (via JOIN — same pattern
    ///   as the P2 aggregate backfill; portable across dev/staging/prod
    ///   where DisplayName may differ).</item>
    ///   <item>Normalize legacy PascalCase values in both projection
    ///   tables to lowercase so the FE (broadened to <c>SourceSystem = string</c>)
    ///   renders a single canonical value across old + new rows.</item>
    /// </list>
    ///
    /// <para><b>Rebuild path (UpsertSql) already writes new columns.</b>
    /// This migration takes care of what the projector wouldn't touch:
    /// existing rows whose current values were written in PascalCase.</para>
    ///
    /// <para><b>OrderFacts:</b> DisplayName column NOT added — BI queries
    /// GROUP BY the key, not the display, and adding a snapshot column
    /// to the fact table would double the storage cost for no analytic
    /// benefit. The lowercase normalize still runs so per-source BI
    /// panels don't split "Oms" and "oms" into two rows.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260630050000_NormalizeProjectionsSourceSystemCase")]
    public partial class NormalizeProjectionsSourceSystemCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Widen OrderListView.SourceSystem column so long slugs fit.
            migrationBuilder.AlterColumn<string>(
                name: "SourceSystem",
                schema: "deliveryorder",
                table: "OrderListView",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            // 2. Add DisplayName snapshot column (nullable — legacy rows
            //    may have no matching SystemClient after admin deletes).
            migrationBuilder.AddColumn<string>(
                name: "SourceSystemDisplayName",
                schema: "deliveryorder",
                table: "OrderListView",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // 3. Normalize PascalCase → lowercase in OrderListView.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderListView""
                   SET ""SourceSystem"" = lower(""SourceSystem"");
            ");

            // 4. Backfill DisplayName by JOINing with iam.SystemClients
            //    on the now-lowercase key.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderListView"" AS o
                   SET ""SourceSystemDisplayName"" = sc.""DisplayName""
                  FROM iam.""SystemClients"" AS sc
                 WHERE o.""SourceSystem"" = sc.""Key""
                   AND o.""SourceSystemDisplayName"" IS NULL;
            ");

            // 5. Normalize OrderFacts (BI). No DisplayName column here
            //    — see class summary for rationale.
            migrationBuilder.Sql(@"
                UPDATE bi.""OrderFacts""
                   SET ""SourceSystem"" = lower(""SourceSystem"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort rollback — restore PascalCase for the four
            // well-known enum values on OrderFacts + OrderListView.
            // Rows written after P5 with a dynamic slug (wms-acme, etc.)
            // stay lowercase because there is no PascalCase equivalent.
            migrationBuilder.Sql(@"
                UPDATE bi.""OrderFacts"" SET ""SourceSystem"" =
                    CASE ""SourceSystem""
                        WHEN 'manual' THEN 'Manual'
                        WHEN 'oms'    THEN 'Oms'
                        WHEN 'sap'    THEN 'Sap'
                        WHEN 'erp'    THEN 'Erp'
                        ELSE ""SourceSystem""
                    END;
            ");

            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderListView"" SET ""SourceSystem"" =
                    CASE ""SourceSystem""
                        WHEN 'manual' THEN 'Manual'
                        WHEN 'oms'    THEN 'Oms'
                        WHEN 'sap'    THEN 'Sap'
                        WHEN 'erp'    THEN 'Erp'
                        ELSE ""SourceSystem""
                    END;
            ");

            migrationBuilder.DropColumn(
                name: "SourceSystemDisplayName",
                schema: "deliveryorder",
                table: "OrderListView");

            migrationBuilder.AlterColumn<string>(
                name: "SourceSystem",
                schema: "deliveryorder",
                table: "OrderListView",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
