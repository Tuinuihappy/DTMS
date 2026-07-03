using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Manual.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-3c — revert the zone-based operator scoping introduced
    ///   in PR-3. Business decision: all Manual operators can serve every
    ///   WMS location in-plant, so the ServiceZones filter is unnecessary
    ///   complexity. ManualDispatchStrategy now picks any Active + idle
    ///   operator regardless of pickup location.
    /// REMOVES: ServiceZones jsonb column + IX_Operators_ServiceZones_gin.
    /// REVERSIBLE: Down() re-adds the column with default [] — data lost.
    /// </summary>
    [DbContext(typeof(TransportManualDbContext))]
    [Migration("20260702170000_DropOperatorServiceZones")]
    public partial class DropOperatorServiceZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS transportmanual.\"IX_Operators_ServiceZones_gin\";");

            migrationBuilder.DropColumn(
                name: "ServiceZones",
                schema: "transportmanual",
                table: "Operators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceZones",
                schema: "transportmanual",
                table: "Operators",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Operators_ServiceZones_gin\" " +
                "ON transportmanual.\"Operators\" USING GIN (\"ServiceZones\" jsonb_path_ops);");
        }
    }
}
