using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Indexes dispatch.Trips.DeliveryOrderId. Planning now reads an order's
    /// trips on every plan (to pick each group's attempt), and Reopen,
    /// Redispatch, the order drawer and the cancel dialog already did — all
    /// of them were sequential scans of the whole table. Plain CREATE INDEX:
    /// the migrator runs while the api is stopped, so the brief write lock
    /// blocks nothing.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20261022100000_AddTripsDeliveryOrderIdIndex")]
    public partial class AddTripsDeliveryOrderIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Trips_DeliveryOrderId\" " +
                "ON dispatch.\"Trips\" (\"DeliveryOrderId\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS dispatch.\"IX_Trips_DeliveryOrderId\";");
        }
    }
}
