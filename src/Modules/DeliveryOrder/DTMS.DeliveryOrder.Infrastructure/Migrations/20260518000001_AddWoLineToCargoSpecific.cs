using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260518000001_AddWoLineToCargoSpecific")]
    public partial class AddWoLineToCargoSpecific : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Wo",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Line",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Wo",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Line",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
