using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemTemperatureRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TemperatureRange is a nullable owned value object on Item. Both
            // bounds are independently nullable; when both columns are NULL the
            // item is treated as ambient — which is the default for every
            // existing row, so no backfill is needed.
            migrationBuilder.AddColumn<double>(
                name: "Temperature_MinC",
                schema: "deliveryorder",
                table: "Items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Temperature_MaxC",
                schema: "deliveryorder",
                table: "Items",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Temperature_MaxC",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Temperature_MinC",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
