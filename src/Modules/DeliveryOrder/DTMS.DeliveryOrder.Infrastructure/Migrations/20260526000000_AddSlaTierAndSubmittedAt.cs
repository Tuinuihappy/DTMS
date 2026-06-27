using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaTierAndSubmittedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Bronze");

            migrationBuilder.AddColumn<System.DateTime>(
                name: "SubmittedAt",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
