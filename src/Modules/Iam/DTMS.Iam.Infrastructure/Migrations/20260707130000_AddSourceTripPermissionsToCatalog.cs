using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Registers the source-system trip-lifecycle permission codes in the
    /// <c>iam.Permissions</c> catalog so they surface in the admin permission
    /// list. The grants already exist in <c>iam.SystemClientPermissions</c>
    /// (seeded by SeedSourceSystemTripPermissions + SeedSourceRobotPassPermission),
    /// but the catalog row is what the admin UI lists — without it the codes are
    /// enforced yet invisible. Mirrors how the source order codes were catalogued
    /// in AddIamSystemClientsPhaseS2 (Module = 'Source', oms-scoped entries).
    ///
    /// <para>These codes gate machine callers under <c>/api/v1/source/trips/*</c>
    /// (matched against SystemClientPermissions by SourceSystemPermissionHandler);
    /// they are NOT role permissions. All inserts idempotent
    /// (<c>ON CONFLICT DO NOTHING</c>); Down removes only these five codes.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260707130000_AddSourceTripPermissionsToCatalog")]
    public partial class AddSourceTripPermissionsToCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  ('dtms:source:oms:trip:acknowledge',          'Acknowledge (start) an OMS source trip.',        'Source', now()),
                  ('dtms:source:oms:trip:pickup',               'Report pickup on an OMS source trip.',           'Source', now()),
                  ('dtms:source:oms:trip:drop',                 'Report drop on an OMS source trip.',             'Source', now()),
                  ('dtms:source:oms:trip:complete',             'Complete an OMS source trip.',                   'Source', now()),
                  ('dtms:source:oms:trip:acknowledge-robot-pass','Acknowledge robot PASS on an OMS source trip.', 'Source', now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions""
                 WHERE ""Code"" IN (
                   'dtms:source:oms:trip:acknowledge',
                   'dtms:source:oms:trip:pickup',
                   'dtms:source:oms:trip:drop',
                   'dtms:source:oms:trip:complete',
                   'dtms:source:oms:trip:acknowledge-robot-pass'
                 );
            ");
        }
    }
}
