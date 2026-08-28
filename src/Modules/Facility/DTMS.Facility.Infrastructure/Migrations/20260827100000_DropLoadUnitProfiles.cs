using System;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <summary>
    /// CARRIER TRACKING P0.1 — drops the LoadUnitProfile catalogue.
    ///
    /// <para>Why it's dead: the entity had exactly two readers in the whole
    /// solution — <c>GetLoadUnitProfilesQuery</c> and
    /// <c>RegisterLoadUnitProfileCommand</c>, i.e. its own CRUD. No module ever
    /// consumed it. In particular <c>Item.LoadUnitProfileCode</c> is NOT a
    /// foreign key into this table and never was validated against it
    /// (DraftItemDtoValidator only enforces MaximumLength(50)), so the column
    /// keeps working unchanged as free text — which is what it has always been
    /// in practice. Removing the column is a separate, breaking piece of work
    /// tracked as Phase 6 of the carrier-tracking plan.</para>
    ///
    /// <para>The table also carries no foreign keys in either direction:
    /// LoadUnitProfiles.CarrierTypeCode is a plain indexed string, not an FK to
    /// CarrierTypeProfiles (see 20260506082818_AddCarrierTypeAndLoadUnitProfiles),
    /// so the drop has no referential fallout.</para>
    ///
    /// PRE-FLIGHT (verified before this was written): zero rows in
    ///   facility."LoadUnitProfiles", and zero hits in iam."SystemRequestLog"
    ///   for the /load-unit-profiles path — no system client ever called it.
    /// REVERSIBLE: Down() recreates the exact schema of record (table +
    ///   both indexes) from 20260506082818. Data is not recoverable, which is
    ///   acceptable because the table is empty.
    /// </summary>
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260827100000_DropLoadUnitProfiles")]
    public partial class DropLoadUnitProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LoadUnitProfiles", schema: "facility");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
