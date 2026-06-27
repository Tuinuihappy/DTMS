using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Snapshots the owning order's RequestedTransportMode (Amr/Manual/Fleet)
    /// onto each dispatch.TripItems row so the trip-items endpoint can
    /// surface routing context without a cross-module join. Nullable —
    /// pre-V1.4 snapshots and pre-existing rows carry NULL.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260616090000_AddTripItemOrderTransportMode")]
    public partial class AddTripItemOrderTransportMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderTransportMode",
                schema: "dispatch",
                table: "TripItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderTransportMode",
                schema: "dispatch",
                table: "TripItems");
        }
    }
}
