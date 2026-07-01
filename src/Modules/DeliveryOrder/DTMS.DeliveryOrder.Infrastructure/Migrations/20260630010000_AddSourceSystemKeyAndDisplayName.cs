using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P1 of SourceSystem enum→dynamic-key migration. Adds two
    /// nullable columns alongside the existing <c>SourceSystem</c>
    /// varchar(20) enum column:
    /// <list type="bullet">
    ///   <item><c>SourceSystemKey varchar(50) NULL</c> — soft-FK to
    ///   <c>iam.SystemClients.Key</c>. Populated by the P2 backfill
    ///   migration then by every new write from P4 onward.</item>
    ///   <item><c>SourceSystemDisplayName varchar(200) NULL</c> —
    ///   snapshot of the SystemClient's DisplayName at create time.
    ///   Immutable per row (audit-preserving).</item>
    /// </list>
    ///
    /// <para>No index created in this migration — P2 adds
    /// <c>IX_DeliveryOrders_SourceSystemKey_OrderRef</c> alongside the
    /// legacy unique index (dual-window). Both indexes coexist through
    /// P5, then P6 drops the legacy one.</para>
    ///
    /// <para>Existing rows remain unaffected: the enum column stays
    /// authoritative for read paths until P3 flips them over.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260630010000_AddSourceSystemKeyAndDisplayName")]
    public partial class AddSourceSystemKeyAndDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceSystemKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystemDisplayName",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceSystemDisplayName",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "SourceSystemKey",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
