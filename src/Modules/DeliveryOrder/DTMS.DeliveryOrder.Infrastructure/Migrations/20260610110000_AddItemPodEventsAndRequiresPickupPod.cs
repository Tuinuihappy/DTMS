using System;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Restructure POD evidence into a per-checkpoint child table so
    /// items can carry both pickup and drop scans:
    ///
    ///   • New: ItemPodEvents (Id, ItemId FK, ScanType, ScannedAt,
    ///          ScannedBy, Method, Reference) with UNIQUE (ItemId, ScanType)
    ///   • New: DeliveryOrders.RequiresPickupPod (nullable bool, default false).
    ///          Pickup POD is opt-in — existing orders inherit the safe
    ///          default (off) so behaviour does not change.
    ///   • Backfill: every Item with a non-null PodScannedAt becomes one
    ///          ItemPodEvent row with ScanType='Drop' (the flat columns
    ///          only ever held drop POD).
    ///   • Drop: the now-redundant Items.PodScanned* columns.
    ///
    /// DroppedOffAt stays on Items — it's the SLA clock anchor stamped
    /// by the vendor drop sub-task, not a POD scan.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260610110000_AddItemPodEventsAndRequiresPickupPod")]
    public partial class AddItemPodEventsAndRequiresPickupPod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New per-checkpoint POD table
            migrationBuilder.CreateTable(
                name: "ItemPodEvents",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScannedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPodEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemPodEvents_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "deliveryorder",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemPodEvents_ItemId_ScanType",
                schema: "deliveryorder",
                table: "ItemPodEvents",
                columns: new[] { "ItemId", "ScanType" },
                unique: true);

            // 2. Pickup POD policy column (opt-in, default false). Nullable
            //    matches RequiresDropPod's tri-state semantics: null falls
            //    back to OrderTemplate.RequiresPickupPod.
            migrationBuilder.AddColumn<bool>(
                name: "RequiresPickupPod",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "boolean",
                nullable: true,
                defaultValue: false);

            // 3. Backfill existing drop POD scans. PodMethod is NOT NULL
            //    in the destination — items that have a PodScannedAt but
            //    no method shouldn't exist, but coalesce to "Manual" to
            //    be defensive (and surface via PodReference='backfilled').
            migrationBuilder.Sql(@"
INSERT INTO deliveryorder.""ItemPodEvents""
    (""Id"", ""ItemId"", ""ScanType"", ""ScannedAt"", ""ScannedBy"", ""Method"", ""Reference"")
SELECT gen_random_uuid(),
       ""Id"",
       'Drop',
       ""PodScannedAt"",
       COALESCE(""PodScannedBy"", 'backfill'),
       COALESCE(""PodMethod"", 'Manual'),
       ""PodReference""
FROM deliveryorder.""Items""
WHERE ""PodScannedAt"" IS NOT NULL;
");

            // 4. Drop the now-redundant flat columns.
            migrationBuilder.DropColumn(name: "PodReference",  schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "PodMethod",     schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "PodScannedBy",  schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "PodScannedAt",  schema: "deliveryorder", table: "Items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the flat columns first
            migrationBuilder.AddColumn<DateTime>(
                name: "PodScannedAt",
                schema: "deliveryorder",
                table: "Items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PodScannedBy",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PodMethod",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PodReference",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Restore drop scans (last write wins if duplicates exist,
            // which can't happen because of the unique index above).
            migrationBuilder.Sql(@"
UPDATE deliveryorder.""Items"" i
SET ""PodScannedAt"" = e.""ScannedAt"",
    ""PodScannedBy"" = e.""ScannedBy"",
    ""PodMethod""    = e.""Method"",
    ""PodReference"" = e.""Reference""
FROM deliveryorder.""ItemPodEvents"" e
WHERE e.""ItemId"" = i.""Id"" AND e.""ScanType"" = 'Drop';
");

            migrationBuilder.DropTable(name: "ItemPodEvents", schema: "deliveryorder");
            migrationBuilder.DropColumn(name: "RequiresPickupPod", schema: "deliveryorder", table: "DeliveryOrders");
        }
    }
}
