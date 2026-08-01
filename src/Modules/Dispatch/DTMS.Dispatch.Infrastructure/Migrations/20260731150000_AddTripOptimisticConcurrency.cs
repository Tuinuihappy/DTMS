using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the xmin optimistic-concurrency token to Trips (model level
    /// only). Trips are written concurrently by HTTP commands, the RIOT3
    /// webhook, the reconciler and consumers; the 2026-07-29 E2E showed a
    /// command and its vendor-echo webhook committing ~70ms apart, both
    /// passing the in-memory guards and emitting duplicate domain events.
    ///
    /// xmin is a PostgreSQL system column maintained automatically by the
    /// database engine. No DDL is required — EF Core tracks it as a
    /// concurrency token at the model level only (same as DeliveryOrder's
    /// 20260504112157_AddOptimisticConcurrency).
    ///
    /// REVERSIBLE: trivially — nothing to undo in the database.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260731150000_AddTripOptimisticConcurrency")]
    public partial class AddTripOptimisticConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
