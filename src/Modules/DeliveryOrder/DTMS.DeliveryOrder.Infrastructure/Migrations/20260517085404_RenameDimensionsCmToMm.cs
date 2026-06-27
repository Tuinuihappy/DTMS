using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameDimensionsCmToMm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WidthCm",
                schema: "deliveryorder",
                table: "Items",
                newName: "WidthMm");

            migrationBuilder.RenameColumn(
                name: "LengthCm",
                schema: "deliveryorder",
                table: "Items",
                newName: "LengthMm");

            migrationBuilder.RenameColumn(
                name: "HeightCm",
                schema: "deliveryorder",
                table: "Items",
                newName: "HeightMm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WidthMm",
                schema: "deliveryorder",
                table: "Items",
                newName: "WidthCm");

            migrationBuilder.RenameColumn(
                name: "LengthMm",
                schema: "deliveryorder",
                table: "Items",
                newName: "LengthCm");

            migrationBuilder.RenameColumn(
                name: "HeightMm",
                schema: "deliveryorder",
                table: "Items",
                newName: "HeightCm");
        }
    }
}
