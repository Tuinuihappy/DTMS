using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadUnitProfileCodeToItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoadUnitProfileCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoadUnitProfileCode",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
