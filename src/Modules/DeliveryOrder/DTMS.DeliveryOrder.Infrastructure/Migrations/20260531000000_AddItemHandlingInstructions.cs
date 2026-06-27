using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemHandlingInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stored as a Postgres text[] of enum names (e.g. {Fragile,ThisSideUp}).
            // Required + default '{}' so every existing row reads as "no special
            // handling" without a separate backfill step.
            migrationBuilder.AddColumn<string[]>(
                name: "HandlingInstructions",
                schema: "deliveryorder",
                table: "Items",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HandlingInstructions",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
