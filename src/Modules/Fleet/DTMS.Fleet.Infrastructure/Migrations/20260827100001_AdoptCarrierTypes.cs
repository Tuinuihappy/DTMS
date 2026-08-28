using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <summary>
    /// CARRIER TRACKING P0.2 — relocates the carrier-type catalogue from
    /// Facility to Fleet (ADR-019) and renames it to match Fleet's existing
    /// <c>Vehicle</c> / <c>VehicleType</c> pairing.
    ///
    /// <para>Why Fleet: a carrier is a movable asset with a lifecycle, status,
    /// maintenance, and utilization — every one of which Fleet already models
    /// for vehicles and Facility models for nothing. Facility, after the
    /// 2026-08-14 dead-domain purge, is exactly maps and stations: static site
    /// topology. See ADR-019 §Decision A.</para>
    ///
    /// <para><b>Fleet owns all the DDL for this move.</b> The paired Facility
    /// migration (20260827100002_ReleaseCarrierTypeProfiles) is deliberately a
    /// no-op that only drops the entity from Facility's model snapshot. That
    /// asymmetry is the point: with no DDL on the Facility side there is no way
    /// for a DropTable to slip in and destroy the data, and the two migrations
    /// can be applied in either order.</para>
    ///
    /// <para>PostgreSQL does NOT rename indexes or constraints when a table is
    /// renamed, so both are renamed explicitly — otherwise EF's snapshot would
    /// expect PK_CarrierTypes while the database still held
    /// PK_CarrierTypeProfiles, and the next migration touching this table would
    /// generate against the wrong name.</para>
    ///
    /// <para>No compatibility view is created: this deployment is stop-start,
    /// so no process running the old assembly outlives the migration.</para>
    ///
    /// REVERSIBLE: Down() reverses every rename before moving the table back,
    ///   so Facility's snapshot names line up again. Data is untouched in both
    ///   directions — SET SCHEMA and RENAME are metadata-only operations.
    /// </summary>
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260827100001_AdoptCarrierTypes")]
    public partial class AdoptCarrierTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE facility.""CarrierTypeProfiles"" SET SCHEMA fleet;
                ALTER TABLE fleet.""CarrierTypeProfiles"" RENAME TO ""CarrierTypes"";
                ALTER TABLE fleet.""CarrierTypes"" RENAME COLUMN ""AMRCapability"" TO ""AmrCapability"";
                ALTER INDEX fleet.""IX_CarrierTypeProfiles_Code"" RENAME TO ""IX_CarrierTypes_Code"";
                ALTER TABLE fleet.""CarrierTypes"" RENAME CONSTRAINT ""PK_CarrierTypeProfiles"" TO ""PK_CarrierTypes"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE fleet.""CarrierTypes"" RENAME CONSTRAINT ""PK_CarrierTypes"" TO ""PK_CarrierTypeProfiles"";
                ALTER INDEX fleet.""IX_CarrierTypes_Code"" RENAME TO ""IX_CarrierTypeProfiles_Code"";
                ALTER TABLE fleet.""CarrierTypes"" RENAME COLUMN ""AmrCapability"" TO ""AMRCapability"";
                ALTER TABLE fleet.""CarrierTypes"" RENAME TO ""CarrierTypeProfiles"";
                ALTER TABLE fleet.""CarrierTypeProfiles"" SET SCHEMA facility;
            ");
        }
    }
}
