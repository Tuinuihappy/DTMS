using System;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// POD (Proof of Delivery) feature:
    ///   • Items get DroppedOffAt + 4 POD audit columns. DroppedOff is a
    ///     new ItemStatus value but the enum is stored as string in
    ///     existing schemas so no column-type change.
    ///   • Orders get RequiresPod (nullable bool) — when true, the
    ///     TripCompleted consumer leaves DroppedOff items alone instead
    ///     of jumping them to Delivered; the operator must POST
    ///     /delivery-orders/{id}/items/{itemId}/pod-scan.
    ///
    /// All new columns nullable; existing rows continue to behave as
    /// before (RequiresPod null = effectively false until template
    /// resolution lands).
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260606000000_AddItemPodAndOrderRequiresPod")]
    public partial class AddItemPodAndOrderRequiresPod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresPod",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DroppedOffAt",
                schema: "deliveryorder",
                table: "Items",
                type: "timestamp with time zone",
                nullable: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PodReference",  schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "PodMethod",     schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "PodScannedBy",  schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "PodScannedAt",  schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "DroppedOffAt",  schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "RequiresPod",   schema: "deliveryorder", table: "DeliveryOrders");
        }
    }
}
