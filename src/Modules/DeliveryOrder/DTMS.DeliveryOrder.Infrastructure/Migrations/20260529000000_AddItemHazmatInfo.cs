using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemHazmatInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HazmatInfo is a nullable owned value object on Item. When both
            // columns are NULL the item is treated as non-hazardous — which
            // is the default for every existing row, so no backfill is needed.
            migrationBuilder.AddColumn<string>(
                name: "Hazmat_ClassCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hazmat_PackingGroup",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hazmat_PackingGroup",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Hazmat_ClassCode",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
