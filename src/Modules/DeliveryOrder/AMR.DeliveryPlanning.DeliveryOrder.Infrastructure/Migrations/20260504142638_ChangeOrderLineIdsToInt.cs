using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOrderLineIdsToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE deliveryorder."OrderLines"
                    ALTER COLUMN "WorkOrderId" DROP DEFAULT,
                    ALTER COLUMN "ItemId" DROP DEFAULT;
                ALTER TABLE deliveryorder."OrderLines"
                    ALTER COLUMN "WorkOrderId" TYPE integer USING "WorkOrderId"::integer,
                    ALTER COLUMN "ItemId" TYPE integer USING "ItemId"::integer;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WorkOrderId",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ItemId",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
