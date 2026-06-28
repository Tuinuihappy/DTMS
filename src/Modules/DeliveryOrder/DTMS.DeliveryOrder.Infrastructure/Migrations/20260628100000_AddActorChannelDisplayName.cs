using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: S.1 follow-up — propagates the structured ActorContext
    /// (Phase S.1) down into the audit + activity read models so the
    /// admin UI can show Channel (ManualWeb / OperatorPwa / SystemApi /
    /// InternalJob) and DisplayName next to the bare ActorId.
    /// PURPOSE: Adds nullable Channel + DisplayName columns to
    ///   deliveryorder.OrderActivity and deliveryorder.OrderAuditEvents.
    ///   Nullable so historic rows (pre-S.1) stay valid.
    /// DEPENDS ON: 20260623130000_AddItemWarehouseIds (last DO migration).
    /// REVERSIBLE: Yes — Down() drops all four columns cleanly.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260628100000_AddActorChannelDisplayName")]
    public partial class AddActorChannelDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "deliveryorder",
                table: "OrderActivity",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "deliveryorder",
                table: "OrderActivity",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "deliveryorder",
                table: "OrderAuditEvents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "deliveryorder",
                table: "OrderAuditEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "deliveryorder",
                table: "OrderAuditEvents");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "deliveryorder",
                table: "OrderAuditEvents");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "deliveryorder",
                table: "OrderActivity");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "deliveryorder",
                table: "OrderActivity");
        }
    }
}
