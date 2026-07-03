using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-2 — extend Trips with WMS location snapshot columns.
    /// PURPOSE: Manual/Fleet trips capture the WMS location Ids at dispatch
    ///   time so the operator geofence check + POD scanning read the
    ///   authoritative location (from the WMS snapshot) instead of a
    ///   proxy warehouse row.
    /// DEPENDS ON: prior Dispatch migrations; DeliveryOrder PR-2 migration
    ///   (20260702140000) — items must have location Ids before trips can
    ///   reference them.
    /// REVERSIBLE: Yes — Down() drops the two new columns cleanly.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260702141000_AddTripWmsLocationIds")]
    public partial class AddTripWmsLocationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickupWmsLocationId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropWmsLocationId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            // Partial index — WMS trips are the routing key for Manual/Fleet
            // dashboards and operator apps that pivot on pickup location.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Trips_PickupWmsLocationId\" " +
                "ON dispatch.\"Trips\" (\"PickupWmsLocationId\") " +
                "WHERE \"PickupWmsLocationId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS dispatch.\"IX_Trips_PickupWmsLocationId\";");

            migrationBuilder.DropColumn(
                name: "PickupWmsLocationId",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DropWmsLocationId",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
