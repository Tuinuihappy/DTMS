using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadUnitTypeToDeliveryLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "DeliveryLegs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Carton");

            // Remove DB-level default after backfill so new rows must always supply a value
            migrationBuilder.AlterColumn<string>(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "DeliveryLegs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                defaultValue: "Carton");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "DeliveryLegs");
        }
    }
}
