using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAmendmentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New rows default to version 1 (full OrderSnapshotV1 payload).
            // Existing rows predate P1-10 and contain the narrow legacy shape
            // (only ServiceWindow + OrderStatus) — backfill them to 0 so a
            // future reader can branch on version when deserializing.
            migrationBuilder.AddColumn<int>(
                name: "AmendmentVersion",
                schema: "deliveryorder",
                table: "OrderAmendments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderAmendments""
                SET ""AmendmentVersion"" = 0
                WHERE ""AmendedAt"" < NOW() - INTERVAL '1 second';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmendmentVersion",
                schema: "deliveryorder",
                table: "OrderAmendments");
        }
    }
}
