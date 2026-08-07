using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Iam.Presentation;

// Admin surface for the Permission System (Phase B). All endpoints
// require dtms:iam:* permissions.
// User permissions are owned by External Auth (the JWT's permission
// claim) — the role → permission mapping surface that used to live here
// was removed 2026-08-01. The audit log remains for history and for
// system-client permission mutations.
public static class IamEndpoints
{
    public static void MapIamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/iam")
            .WithTags("Iam")
            .RequireAuthorization();

        MapPermissionEndpoints(group);
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

    // ── Permissions catalog (code-served, read-only) ──────────────────────
    // The catalog IS the code — `Permissions.All` is the enforcement source
    // of truth, so the admin UI reads it straight from there. This is
    // reset-safe (no DB seed to keep in sync) and cannot drift from what the
    // code actually checks. Permissions are added by shipping a new
    // PermissionDefinition, never at runtime — hence no write endpoints.
    private static void MapPermissionEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/permissions", () =>
            Results.Ok(Permissions.All
                .OrderBy(p => p.Module, StringComparer.Ordinal)
                .ThenBy(p => p.Code, StringComparer.Ordinal)
                .Select(p => new PermissionDto(p.Code, p.Description, p.Module))))
            .RequirePermission(Permissions.Iam.PermissionRead);
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
            }).RequirePermission(Permissions.Iam.AuditRead);
    }

}

// ── DTOs ─────────────────────────────────────────────────────────────────
public record PermissionDto(string Code, string Description, string Module);

public record AuditLogEntryDto(
    Guid Id, DateTime OccurredAt, string ActorEmployeeId, string Action,
    string? Role, string? PermissionCode, string? Details);

public record PrincipalPermissionsDto(IReadOnlyList<string> Permissions);
