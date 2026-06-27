using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Adds VendorPauseSource to dispatch.Trips so the resume handler knows
    /// which RIOT3 command to send. TASK_HELD pairs with
    /// CMD_ORDER_CONTINUE_FROM_HELD; TASK_HANG pairs with
    /// CMD_ORDER_CONTINUE_FROM_HANG. Sending the wrong one returns E639999
    /// "multi-level template fill error". Nullable — only set while
    /// Status == Paused, cleared on Resume.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260622030000_AddTripVendorPauseSource")]
    public partial class AddTripVendorPauseSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorPauseSource",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorPauseSource",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
