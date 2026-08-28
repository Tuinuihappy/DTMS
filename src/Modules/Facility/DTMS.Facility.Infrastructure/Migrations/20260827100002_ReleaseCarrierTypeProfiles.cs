using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <summary>
    /// CARRIER TRACKING P0.2 — Facility's half of the carrier-type relocation
    /// (ADR-019). Intentionally does NO schema work.
    ///
    /// <para><b>Do not add a DropTable here.</b> The table is not being dropped,
    /// it is being handed to Fleet, and the whole DDL lives in
    /// 20260827100001_AdoptCarrierTypes on the Fleet context. This migration
    /// exists purely so the entity leaves FacilityDbContextModelSnapshot with a
    /// history row explaining why.</para>
    ///
    /// <para>Keeping this side empty is what makes the pair order-independent:
    /// all 8+ DbContexts share public.__EFMigrationsHistory and the migrator
    /// applies them per-context, so a migration that assumed "Fleet ran first"
    /// would be a latent hazard. With no DDL here, there is nothing to order.</para>
    ///
    /// REVERSIBLE: Down() is also empty — reversing the move is Fleet's
    ///   migration's job, and it puts the table back in the facility schema
    ///   under its original names.
    /// </summary>
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260827100002_ReleaseCarrierTypeProfiles")]
    public partial class ReleaseCarrierTypeProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see class summary.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see class summary.
        }
    }
}
