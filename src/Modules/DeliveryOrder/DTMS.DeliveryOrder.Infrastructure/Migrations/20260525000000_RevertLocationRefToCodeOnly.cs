using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RevertLocationRefToCodeOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the StationId columns added by AddLocationRefValueObject — no longer needed
            // now that LocationRef has been reverted to a plain code string. Resolved station
            // GUIDs continue to live in Item.PickupStationId / Item.DropStationId.
            migrationBuilder.DropColumn(
                name: "DropLocationStationId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PickupLocationStationId",
                schema: "deliveryorder",
                table: "Items");

            // Promote the code columns back to NOT NULL — every item must reference a station by code.
            migrationBuilder.AlterColumn<string>(
                name: "PickupLocationCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DropLocationCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PickupLocationCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "DropLocationCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<System.Guid>(
                name: "DropLocationStationId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<System.Guid>(
                name: "PickupLocationStationId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);
        }
    }
}
