using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Manual.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-3b — cleanup after the zone-based dispatch cutover.
    ///   1. Drop <c>Operators.PrimaryWarehouseId</c> — ManualDispatchStrategy
    ///      no longer reads it (ServiceZones drives assignment).
    ///   2. Rename <c>GeofenceOverrideRequests.ExpectedWarehouseId</c> to
    ///      <c>ExpectedWmsLocationId</c> — the leg the override applies to
    ///      is now a WMS location, not a warehouse Guid.
    /// REVERSIBLE: Down() re-adds the column and renames the second back.
    /// </summary>
    [DbContext(typeof(TransportManualDbContext))]
    [Migration("20260702162000_DropOperatorPrimaryWarehouseAndRenameOverrideLocation")]
    public partial class DropOperatorPrimaryWarehouseAndRenameOverrideLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryWarehouseId",
                schema: "transportmanual",
                table: "Operators");

            migrationBuilder.RenameColumn(
                name: "ExpectedWarehouseId",
                schema: "transportmanual",
                table: "GeofenceOverrideRequests",
                newName: "ExpectedWmsLocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExpectedWmsLocationId",
                schema: "transportmanual",
                table: "GeofenceOverrideRequests",
                newName: "ExpectedWarehouseId");

            migrationBuilder.AddColumn<System.Guid>(
                name: "PrimaryWarehouseId",
                schema: "transportmanual",
                table: "Operators",
                type: "uuid",
                nullable: true);
        }
    }
}
