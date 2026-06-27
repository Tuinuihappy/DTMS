using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveCargoTypeToItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable first so we can backfill from the order before applying NOT NULL
            migrationBuilder.AddColumn<string>(
                name: "CargoType",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE deliveryorder."Items" i
                SET "CargoType" = o."CargoType"
                FROM deliveryorder."DeliveryOrders" o
                WHERE i."DeliveryOrderId" = o."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "CargoType",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "CargoType",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CargoType",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.AddColumn<string>(
                name: "CargoType",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }
    }
}
