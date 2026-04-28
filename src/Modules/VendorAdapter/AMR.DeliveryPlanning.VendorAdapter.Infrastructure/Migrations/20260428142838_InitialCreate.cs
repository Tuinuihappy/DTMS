using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vendoradapter");

            migrationBuilder.CreateTable(
                name: "ActionCatalogEntries",
                schema: "vendoradapter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleTypeKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CanonicalAction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AdapterKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VendorParamsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionCatalogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionCatalogEntries_VehicleTypeKey_CanonicalAction",
                schema: "vendoradapter",
                table: "ActionCatalogEntries",
                columns: new[] { "VehicleTypeKey", "CanonicalAction" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionCatalogEntries",
                schema: "vendoradapter");
        }
    }
}
