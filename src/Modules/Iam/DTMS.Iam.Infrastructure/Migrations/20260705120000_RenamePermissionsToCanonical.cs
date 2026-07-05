using System.Linq;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// ADR-017 — rename 30 permission codes to the canonical
    /// <c>dtms:&lt;module&gt;:&lt;resource&gt;:&lt;verb&gt;</c> grammar (Dispatch,
    /// DeliveryOrder, Fleet, Planning, Reporting). Facility / IAM / operator /
    /// the source-system scheme were already compliant and are untouched.
    ///
    /// <para>Grants store the code as a string with NO foreign key and NO
    /// cascade (see IamDbContext), so the rename must update the catalog PK
    /// (<c>iam.Permissions.Code</c>) AND every live grant
    /// (<c>iam.RolePermissions</c>, <c>iam.SystemClientPermissions</c>) AND any
    /// <c>:*</c> wildcard grant under a renamed prefix — all in one
    /// transaction. Admin's <c>dtms:*</c> is unaffected (still matches
    /// everything).</para>
    ///
    /// <para>Grant updates are PK-collision safe (update only when the target
    /// row does not already exist for that principal, then delete the leftover
    /// old row) so the migration is idempotent. A fail-fast assertion aborts
    /// the transaction if any source code survives in the catalog.</para>
    ///
    /// <para><b>Audit history is intentionally NOT rewritten.</b> The historical
    /// <c>PermissionAuditLog.PermissionCode</c> values record what code was
    /// acted on at the time and must stay truthful; rewriting them would be
    /// revisionism. Instead a single <c>rename-canonical</c> audit row records
    /// that this migration ran.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260705120000_RenamePermissionsToCanonical")]
    public partial class RenamePermissionsToCanonical : Migration
    {
        // old → new. Down() runs these reversed.
        private static readonly (string Old, string New)[] Renames =
        {
            // Dispatch
            ("dtms:trip:read", "dtms:dispatch:trip:read"),
            ("dtms:trip:pause", "dtms:dispatch:trip:pause"),
            ("dtms:trip:acknowledge", "dtms:dispatch:trip:acknowledge"),
            ("dtms:trip:cancel", "dtms:dispatch:trip:cancel"),
            ("dtms:trip:retry", "dtms:dispatch:trip:retry"),
            ("dtms:trip:exception", "dtms:dispatch:exception:raise"),
            ("dtms:trip:pod", "dtms:dispatch:pod:upload"),
            // DeliveryOrder
            ("dtms:order:read", "dtms:deliveryorder:order:read"),
            ("dtms:order:write", "dtms:deliveryorder:order:write"),
            ("dtms:order:submit", "dtms:deliveryorder:order:submit"),
            ("dtms:order:reject", "dtms:deliveryorder:order:reject"),
            ("dtms:order:cancel", "dtms:deliveryorder:order:cancel"),
            ("dtms:order:hold", "dtms:deliveryorder:order:hold"),
            ("dtms:order:reopen", "dtms:deliveryorder:order:reopen"),
            ("dtms:order:abandon", "dtms:deliveryorder:order:abandon"),
            ("dtms:order:redispatch", "dtms:deliveryorder:order:redispatch"),
            ("dtms:order:upstream", "dtms:deliveryorder:order:create-upstream"),
            ("dtms:order:bulk", "dtms:deliveryorder:order:bulk-submit"),
            ("dtms:order:notify-oms", "dtms:deliveryorder:order:notify-oms"),
            ("dtms:order:pod", "dtms:deliveryorder:pod:upload"),
            ("dtms:order:item:read", "dtms:deliveryorder:item:read"),
            // Fleet
            ("dtms:vehicle:read", "dtms:fleet:vehicle:read"),
            ("dtms:vehicle:write", "dtms:fleet:vehicle:write"),
            ("dtms:vehicle:import", "dtms:fleet:vehicle:import"),
            ("dtms:vehicle:maintenance", "dtms:fleet:vehicle:maintain"),
            // Planning
            ("dtms:planning:consolidate", "dtms:planning:consolidation:run"),
            ("dtms:planning:order-template:create", "dtms:planning:order-template:instantiate"),
            // Reporting
            ("dtms:report:read", "dtms:reporting:report:read"),
            ("dtms:report:export", "dtms:reporting:report:export"),
            ("dtms:dashboard:read", "dtms:reporting:dashboard:read"),
        };

        // best-effort remap for any :* wildcard grant under a renamed prefix
        private static readonly (string Old, string New)[] WildcardRenames =
        {
            ("dtms:trip:*", "dtms:dispatch:*"),
            ("dtms:order:*", "dtms:deliveryorder:*"),
            ("dtms:vehicle:*", "dtms:fleet:vehicle:*"),
            ("dtms:report:*", "dtms:reporting:report:*"),
            ("dtms:dashboard:*", "dtms:reporting:dashboard:*"),
        };

        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(BuildSql(Renames, WildcardRenames,
                "ADR-017 canonical permission rename (30 codes)"));

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(BuildSql(
                Renames.Select(r => (r.New, r.Old)).ToArray(),
                WildcardRenames.Select(w => (w.New, w.Old)).ToArray(),
                "ADR-017 canonical permission rename — rolled back"));

        private static string BuildSql((string Old, string New)[] pairs,
            (string Old, string New)[] wildcards, string auditDetails)
        {
            string Rows((string Old, string New)[] ps) =>
                string.Join(",\n                    ", ps.Select(p => $"('{p.Old}', '{p.New}')"));
            var sourceList = string.Join(", ", pairs.Select(p => $"'{p.Old}'"));

            return $@"
                DO $$
                DECLARE r record;
                BEGIN
                    -- exact code renames: catalog PK + live grants (PK-collision safe)
                    FOR r IN SELECT * FROM (VALUES
                    {Rows(pairs)}
                    ) AS t(oldc, newc) LOOP
                        -- catalog PK: collision-safe (rename only when target is
                        -- free, else drop the stale old row) so a partial re-run
                        -- can't throw a duplicate-key on iam.Permissions.
                        UPDATE iam.""Permissions"" SET ""Code"" = r.newc
                          WHERE ""Code"" = r.oldc
                            AND NOT EXISTS (SELECT 1 FROM iam.""Permissions"" x WHERE x.""Code"" = r.newc);
                        DELETE FROM iam.""Permissions"" WHERE ""Code"" = r.oldc;

                        UPDATE iam.""RolePermissions"" rp SET ""PermissionCode"" = r.newc
                          WHERE rp.""PermissionCode"" = r.oldc
                            AND NOT EXISTS (SELECT 1 FROM iam.""RolePermissions"" x
                                            WHERE x.""Role"" = rp.""Role"" AND x.""PermissionCode"" = r.newc);
                        DELETE FROM iam.""RolePermissions"" WHERE ""PermissionCode"" = r.oldc;

                        UPDATE iam.""SystemClientPermissions"" sp SET ""PermissionCode"" = r.newc
                          WHERE sp.""PermissionCode"" = r.oldc
                            AND NOT EXISTS (SELECT 1 FROM iam.""SystemClientPermissions"" x
                                            WHERE x.""SystemKey"" = sp.""SystemKey"" AND x.""PermissionCode"" = r.newc);
                        DELETE FROM iam.""SystemClientPermissions"" WHERE ""PermissionCode"" = r.oldc;
                    END LOOP;

                    -- wildcard grant remaps (Admin dtms:* is unaffected)
                    FOR r IN SELECT * FROM (VALUES
                    {Rows(wildcards)}
                    ) AS t(oldc, newc) LOOP
                        UPDATE iam.""RolePermissions"" rp SET ""PermissionCode"" = r.newc
                          WHERE rp.""PermissionCode"" = r.oldc
                            AND NOT EXISTS (SELECT 1 FROM iam.""RolePermissions"" x
                                            WHERE x.""Role"" = rp.""Role"" AND x.""PermissionCode"" = r.newc);
                        DELETE FROM iam.""RolePermissions"" WHERE ""PermissionCode"" = r.oldc;

                        UPDATE iam.""SystemClientPermissions"" sp SET ""PermissionCode"" = r.newc
                          WHERE sp.""PermissionCode"" = r.oldc
                            AND NOT EXISTS (SELECT 1 FROM iam.""SystemClientPermissions"" x
                                            WHERE x.""SystemKey"" = sp.""SystemKey"" AND x.""PermissionCode"" = r.newc);
                        DELETE FROM iam.""SystemClientPermissions"" WHERE ""PermissionCode"" = r.oldc;
                    END LOOP;

                    -- fail-fast: no source code may survive in the catalog
                    IF EXISTS (SELECT 1 FROM iam.""Permissions"" WHERE ""Code"" IN ({sourceList})) THEN
                        RAISE EXCEPTION 'permission rename incomplete: source codes remain in catalog';
                    END IF;

                    -- record the migration itself; historical audit codes untouched
                    INSERT INTO iam.""PermissionAuditLog""
                        (""Id"", ""OccurredAt"", ""ActorEmployeeId"", ""Action"", ""Role"", ""PermissionCode"", ""Details"")
                    VALUES (gen_random_uuid(), now(), 'system:migration', 'rename-canonical', NULL, NULL, '{auditDetails}');
                END $$;
            ";
        }
    }
}
