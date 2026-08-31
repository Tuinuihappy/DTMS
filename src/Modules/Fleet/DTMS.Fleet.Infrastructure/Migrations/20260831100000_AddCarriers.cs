using System;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <summary>
    /// CARRIER TRACKING P1 — the registry of physical carriers plus their
    /// maintenance history (ADR-019).
    ///
    /// <para><b>FK targets CarrierTypes.Id, not .Code.</b> Code carries only a
    /// unique index; making it an FK principal would require an alternate key,
    /// which EF materializes as a second UNIQUE constraint — and therefore a
    /// second index — beside IX_CarrierTypes_Code. No other table in this
    /// solution uses HasAlternateKey, so this keeps the schema and the modelling
    /// idiom unchanged. Callers still speak in carrier-type *codes*; handlers
    /// resolve them to ids.</para>
    ///
    /// <para><b>UX_Carriers_CarrierCode is unconditional</b> — a code identifies
    /// one physical carrier for all time, including after retirement, so
    /// historical statements about a code can never become ambiguous. Deletion
    /// does free a code, but only carriers with no history whatsoever can be
    /// deleted, so nothing ambiguous can result.</para>
    ///
    /// <para><b>Two partial unique indexes</b> carry invariants that application
    /// checks cannot: at most one open maintenance episode per carrier, and
    /// barcodes unique among the carriers that have one (many may have none).</para>
    ///
    /// REVERSIBLE: Down() drops both tables. Data is lost, which is acceptable
    ///   for brand-new tables nothing else depends on — take a pg_dump first if
    ///   carriers have already been registered.
    /// </summary>
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260831100000_AddCarriers")]
    public partial class AddCarriers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carriers",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CarrierTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaintenanceReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaintenanceSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentLocationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommissionedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetireReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carriers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carriers_CarrierTypes_CarrierTypeId",
                        column: x => x.CarrierTypeId,
                        principalSchema: "fleet",
                        principalTable: "CarrierTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierMaintenanceLog",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierMaintenanceLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierMaintenanceLog_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalSchema: "fleet",
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_CarrierCode",
                schema: "fleet",
                table: "Carriers",
                column: "CarrierCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_Barcode",
                schema: "fleet",
                table: "Carriers",
                column: "Barcode",
                unique: true,
                filter: "\"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_Status_CarrierTypeId",
                schema: "fleet",
                table: "Carriers",
                columns: new[] { "Status", "CarrierTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_CarrierTypeId",
                schema: "fleet",
                table: "Carriers",
                column: "CarrierTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierMaintenanceLog_CarrierId_StartedAt",
                schema: "fleet",
                table: "CarrierMaintenanceLog",
                columns: new[] { "CarrierId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierMaintenanceLog_CarrierId",
                schema: "fleet",
                table: "CarrierMaintenanceLog",
                column: "CarrierId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CarrierMaintenanceLog", schema: "fleet");
            migrationBuilder.DropTable(name: "Carriers", schema: "fleet");
        }
    }
}
