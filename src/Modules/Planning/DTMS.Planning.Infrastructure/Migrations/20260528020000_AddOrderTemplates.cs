using System;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260528020000_AddOrderTemplates")]
    public partial class AddOrderTemplates : Migration
    {
        // Phase 1C: the workflow tier of the OperationTemplate hierarchy.
        // Mirrors RIOT3 /api/v4/order/order-templates payload — name +
        // priority + transportOrder.* + vehicle binding hints. Missions live
        // as a single jsonb document; they're rarely queried independently
        // and we always need them all together when building a dispatch order.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderTemplates",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    StructureType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransportOrderPriority = table.Column<int>(type: "integer", nullable: false),
                    Missions = table.Column<string>(type: "jsonb", nullable: false),
                    AppointVehicleKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AppointVehicleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AppointVehicleGroupKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppointVehicleGroupName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppointQueueWaitArea = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderTemplates_Name_Unique",
                schema: "planning",
                table: "OrderTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderTemplates_IsActive",
                schema: "planning",
                table: "OrderTemplates",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderTemplates",
                schema: "planning");
        }
    }
}
