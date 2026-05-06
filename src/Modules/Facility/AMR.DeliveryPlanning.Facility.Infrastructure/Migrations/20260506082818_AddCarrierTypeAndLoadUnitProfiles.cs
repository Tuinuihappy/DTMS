using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierTypeAndLoadUnitProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierTypeProfiles",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AMRCapability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaxWeightKg = table.Column<double>(type: "double precision", nullable: true),
                    MaxSlots = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierTypeProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoadUnitProfiles",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LengthMm = table.Column<double>(type: "double precision", nullable: false),
                    WidthMm = table.Column<double>(type: "double precision", nullable: false),
                    HeightMm = table.Column<double>(type: "double precision", nullable: false),
                    MaxGrossWeightKg = table.Column<double>(type: "double precision", nullable: false),
                    CarrierTypeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadUnitProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTypeProfiles_Code",
                schema: "facility",
                table: "CarrierTypeProfiles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadUnitProfiles_CarrierTypeCode",
                schema: "facility",
                table: "LoadUnitProfiles",
                column: "CarrierTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_LoadUnitProfiles_Code",
                schema: "facility",
                table: "LoadUnitProfiles",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierTypeProfiles",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "LoadUnitProfiles",
                schema: "facility");
        }
    }
}
