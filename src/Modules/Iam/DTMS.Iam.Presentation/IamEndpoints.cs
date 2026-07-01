using System.Security.Claims;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Iam.Presentation;

// Admin surface for the Permission System (Phase B). All endpoints
// require dtms:iam:* permissions which Admin holds via the dtms:*
// wildcard from Phase A — no role mapping changes needed at deploy.
// Every mutation appends a row to iam.PermissionAuditLog so the UI
// (and any future compliance export) can reconstruct who changed what.
public static class IamEndpoints
{
    public static void MapIamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/iam")
            .WithTags("Iam")
            .RequireAuthorization();

        MapPermissionEndpoints(group);
        MapRoleEndpoints(group);
        MapAuditLogEndpoints(group);
        MapPrincipalEndpoints(group);
    }

    // ── Principal self-introspection (Phase S.6) ─────────────────────────
    // Returns the calling principal's effective permission set so the
    // frontend can gate menu items + page guards client-side. Backend is
    // still the authoritative enforcer — these are claims the framework
    // already stamped via PermissionClaimsTransformer + the SystemClient
    // permission lookup, so we just project them onto JSON.
    private static void MapPrincipalEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/me/permissions", (HttpContext ctx) =>
        {
            var perms = ctx.User
                .FindAll(PermissionClaimsTransformer.PermissionClaimType)
                .Select(c => c.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToArray();

            return Results.Ok(new PrincipalPermissionsDto(perms));
        });
        // No .RequirePermission(...) — RequireAuthorization on the group
        // already forces authenticated user. Any authenticated principal
        // can read its own permission set; that's the point of the endpoint.
    }

    // ── Permissions catalog ──────────────────────────────────────────────
    private static void MapPermissionEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/permissions", async (IPermissionRepository repo, CancellationToken ct) =>
        {
            var items = await repo.ListAllAsync(ct);
            return Results.Ok(items.Select(p => new PermissionDto(p.Code, p.Description, p.Module)));
        }).RequirePermission("dtms:iam:permission:read");

        group.MapPost("/permissions",
            async (CreatePermissionRequest req, HttpContext ctx, IPermissionRepository repo,
                   IAuditLogRepository audit, CancellationToken ct) =>
            {
                if (await repo.GetByCodeAsync(req.Code, ct) is not null)
                    return Results.Conflict(new { error = $"Permission '{req.Code}' already exists." });
                try
                {
                    var permission = new Permission(req.Code, req.Description ?? "", req.Module ?? "");
                    await repo.AddAsync(permission, ct);
                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "permission-created",
                        permissionCode: req.Code,
                        details: System.Text.Json.JsonSerializer.Serialize(req)), ct);
                    return Results.Created($"/api/v1/iam/permissions/{req.Code}",
                        new PermissionDto(permission.Code, permission.Description, permission.Module));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }).RequirePermission("dtms:iam:permission:write");

        group.MapPut("/permissions/{code}",
            async (string code, UpdatePermissionRequest req, HttpContext ctx,
                   IPermissionRepository repo, IAuditLogRepository audit, CancellationToken ct) =>
            {
                var permission = await repo.GetByCodeAsync(code, ct);
                if (permission is null) return Results.NotFound();

                permission.UpdateMetadata(req.Description ?? "", req.Module ?? "");
                await repo.UpdateAsync(permission, ct);
                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "permission-updated",
                    permissionCode: code,
                    details: System.Text.Json.JsonSerializer.Serialize(req)), ct);
                return Results.NoContent();
            }).RequirePermission("dtms:iam:permission:write");

        group.MapDelete("/permissions/{code}",
            async (string code, HttpContext ctx, IPermissionRepository repo,
                   IAuditLogRepository audit, CancellationToken ct) =>
            {
                var permission = await repo.GetByCodeAsync(code, ct);
                if (permission is null) return Results.NotFound();

                await repo.DeleteAsync(code, ct);
                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "permission-deleted",
                    permissionCode: code), ct);
                return Results.NoContent();
            }).RequirePermission("dtms:iam:permission:write");
    }

    // ── Roles + their permission mappings ────────────────────────────────
    private static void MapRoleEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/roles", async (IRoleRepository repo, CancellationToken ct) =>
        {
            var items = await repo.ListAllAsync(ct);
            return Results.Ok(items.Select(r => new RoleDto(r.Name, r.Description, r.IsSystem)));
        }).RequirePermission("dtms:iam:role:read");

        // Returns every permission code mapped to this role — including
        // wildcards. The frontend resolves wildcard ↔ catalog itself so
        // the matrix view can render checked vs. covered-by-wildcard.
        group.MapGet("/roles/{name}/permissions",
            async (string name, IRoleRepository roleRepo, IPermissionRepository permRepo, CancellationToken ct) =>
            {
                if (await roleRepo.GetByNameAsync(name, ct) is null) return Results.NotFound();
                var codes = await permRepo.GetPermissionCodesForRoleAsync(name, ct);
                return Results.Ok(codes);
            }).RequirePermission("dtms:iam:role:read");

        group.MapPost("/roles",
            async (CreateRoleRequest req, HttpContext ctx, IRoleRepository repo,
                   IAuditLogRepository audit, CancellationToken ct) =>
            {
                if (await repo.GetByNameAsync(req.Name, ct) is not null)
                    return Results.Conflict(new { error = $"Role '{req.Name}' already exists." });
                try
                {
                    var role = new Role(req.Name, req.Description ?? "", isSystem: false);
                    await repo.AddAsync(role, ct);
                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "role-created",
                        role: req.Name,
                        details: System.Text.Json.JsonSerializer.Serialize(req)), ct);
                    return Results.Created($"/api/v1/iam/roles/{req.Name}",
                        new RoleDto(role.Name, role.Description, role.IsSystem));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }).RequirePermission("dtms:iam:role:write");

        group.MapDelete("/roles/{name}",
            async (string name, HttpContext ctx, IRoleRepository repo,
                   IAuditLogRepository audit, CancellationToken ct) =>
            {
                var role = await repo.GetByNameAsync(name, ct);
                if (role is null) return Results.NotFound();
                if (role.IsSystem)
                    return Results.BadRequest(new { error = "System roles cannot be deleted." });
                // Self-lockout guard — deleting the role you're currently
                // signed in with cascades all your mappings, so the next
                // request you make would 403 on every protected endpoint.
                if (string.Equals(name, ActorRole(ctx), StringComparison.Ordinal))
                    return Results.BadRequest(new
                    {
                        error = "Refusing to delete the role you are signed in with " +
                                "— sign in as a different role to remove it."
                    });

                await repo.DeleteAsync(name, ct);
                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "role-deleted",
                    role: name), ct);
                return Results.NoContent();
            }).RequirePermission("dtms:iam:role:write");

        group.MapPost("/roles/{name}/permissions/{code}",
            async (string name, string code, HttpContext ctx, IRoleRepository roleRepo,
                   IPermissionRepository permRepo, IAuditLogRepository audit,
                   Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
                   CancellationToken ct) =>
            {
                if (await roleRepo.GetByNameAsync(name, ct) is null) return Results.NotFound(new { error = $"Role '{name}' not found." });
                // Wildcards (e.g. dtms:facility:*) are valid grants but
                // won't be in the Permission catalog — skip the existence
                // check when the code ends with ':*'.
                if (!code.EndsWith(":*", StringComparison.Ordinal)
                    && await permRepo.GetByCodeAsync(code, ct) is null)
                {
                    return Results.NotFound(new { error = $"Permission '{code}' not found." });
                }

                var inserted = await roleRepo.GrantPermissionAsync(name, code, ct);
                if (inserted)
                {
                    // Phase S.8b — evict PermissionClaimsTransformer cache
                    // so users on this role see the grant on their next
                    // request instead of waiting up to 5 minutes.
                    cache.Remove($"iam:perms:{name}");

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "grant",
                        role: name,
                        permissionCode: code), ct);
                }
                return Results.NoContent();
            }).RequirePermission("dtms:iam:role:write");

        group.MapDelete("/roles/{name}/permissions/{code}",
            async (string name, string code, HttpContext ctx, IRoleRepository repo,
                   IAuditLogRepository audit,
                   Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
                   CancellationToken ct) =>
            {
                // Self-lockout guard — the catch-all wildcard is the
                // master key; once the transformer cache is evicted the
                // actor loses every perm, including dtms:iam:role:write
                // needed to undo it. Other revokes on your own role are
                // allowed because the operator can pick a single perm
                // back through another surface; only this case is
                // unrecoverable from the UI.
                if (code == "dtms:*"
                    && string.Equals(name, ActorRole(ctx), StringComparison.Ordinal))
                {
                    return Results.BadRequest(new
                    {
                        error = "Refusing to revoke 'dtms:*' from the role you are " +
                                "signed in with — this would lock you out on next request."
                    });
                }

                var deleted = await repo.RevokePermissionAsync(name, code, ct);
                if (deleted)
                {
                    // Phase S.8b — evict transformer cache so users on this
                    // role lose the revoked permission on their next
                    // request (else it persists in-memory for up to 5 min).
                    cache.Remove($"iam:perms:{name}");

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "revoke",
                        role: name,
                        permissionCode: code), ct);
                }
                return Results.NoContent();
            }).RequirePermission("dtms:iam:role:write");
    }

    // ── Audit log read ───────────────────────────────────────────────────
    private static void MapAuditLogEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/audit-log",
            async (string? actor, string? role, string? action, int? page, int? pageSize,
                   IAuditLogRepository repo, CancellationToken ct) =>
            {
                var p = page is null or <= 0 ? 1 : page.Value;
                var s = pageSize is null or <= 0 or > 200 ? 50 : pageSize.Value;
                var (items, total) = await repo.QueryAsync(actor, role, action, p, s, ct);
                return Results.Ok(new
                {
                    items = items.Select(a => new AuditLogEntryDto(
                        a.Id, a.OccurredAt, a.ActorEmployeeId, a.Action,
                        a.Role, a.PermissionCode, a.Details)),
                    totalCount = total,
                    page = p,
                    pageSize = s,
                });
            }).RequirePermission("dtms:iam:audit:read");
    }

    private static string ActorOrUnknown(HttpContext ctx)
        => ctx.User.FindFirst("EmployeeId")?.Value
           ?? ctx.User.Identity?.Name
           ?? "unknown";

    // Returns the role string carried by the actor's JWT (e.g. "Admin").
    // Self-lockout guards compare this against the role being mutated.
    // Falls back to empty string when no role claim is present, which
    // means the guards become no-ops for service-to-service callers
    // (they shouldn't be hitting these endpoints anyway).
    private static string ActorRole(HttpContext ctx)
        => ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
}

// ── DTOs ─────────────────────────────────────────────────────────────────
public record PermissionDto(string Code, string Description, string Module);
public record CreatePermissionRequest(string Code, string? Description, string? Module);
public record UpdatePermissionRequest(string? Description, string? Module);

public record RoleDto(string Name, string Description, bool IsSystem);
public record CreateRoleRequest(string Name, string? Description);

public record AuditLogEntryDto(
    Guid Id, DateTime OccurredAt, string ActorEmployeeId, string Action,
    string? Role, string? PermissionCode, string? Details);

public record PrincipalPermissionsDto(IReadOnlyList<string> Permissions);
