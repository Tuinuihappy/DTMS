using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Adds Description, QuantityValue, QuantityUom to the
    /// dispatch.TripItems projection so the operator trip-items drawer
    /// can render fulfilment context without a second hop to the
    /// DeliveryOrder side. All three are nullable — pre-V1.3 snapshot
    /// rows simply carry NULLs.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260615200000_AddTripItemDescriptionAndQuantity")]
    public partial class AddTripItemDescriptionAndQuantity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "dispatch",
                table: "TripItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "QuantityValue",
                schema: "dispatch",
                table: "TripItems",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUom",
                schema: "dispatch",
                table: "TripItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuantityUom",
                schema: "dispatch",
                table: "TripItems");

            migrationBuilder.DropColumn(
                name: "QuantityValue",
                schema: "dispatch",
                table: "TripItems");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "dispatch",
                table: "TripItems");
        }
    }
}
