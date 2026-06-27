using System.Security.Claims;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Enums;

namespace DTMS.Api.Auth;

// Phase 4.2 — Runs after authentication on every /api/operator/*
// request. Upserts the DTMS Operator row to match the JWT claims
// (per ADR-014: External Auth owns identity; DTMS mirrors).
//
// Stashes the Operator's internal Guid Id into HttpContext.Items so
// downstream endpoint handlers can take it as a constructor parameter
// via `httpContext.GetOperatorId()` without re-doing the lookup.
//
// Scoped middleware — resolved per request so it can take a scoped
// IOperatorSyncService that owns its own DbContext.
public sealed class OperatorSyncMiddleware : IMiddleware
{
    public const string OperatorIdItemKey = "Operator:Id";

    private readonly IOperatorSyncService _sync;
    private readonly ILogger<OperatorSyncMiddleware> _logger;

    public OperatorSyncMiddleware(IOperatorSyncService sync, ILogger<OperatorSyncMiddleware> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Only act on authenticated requests that hit /api/operator/*.
        // Other paths (admin endpoints, hubs, etc.) pass through untouched
        // so we don't pay the operator-sync overhead per request.
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true ||
            !context.Request.Path.StartsWithSegments("/api/operator", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var employeeCode = user.FindFirstValue("employeeCode") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var displayName = user.FindFirstValue("displayName") ?? user.FindFirstValue(ClaimTypes.Name);
        var roleStr = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(employeeCode) || string.IsNullOrWhiteSpace(displayName))
        {
            _logger.LogWarning("OperatorSyncMiddleware: missing claims on authenticated request; passing through unsynced.");
            await next(context);
            return;
        }

        if (!Enum.TryParse<OperatorRole>(roleStr, ignoreCase: true, out var role))
            role = OperatorRole.Operator;

        Guid? primaryWarehouseId = null;
        var warehouseClaim = user.FindFirstValue("warehouseId") ?? user.FindFirstValue("primaryWarehouseId");
        if (Guid.TryParse(warehouseClaim, out var wid))
            primaryWarehouseId = wid;

        var thumbnailUrl = user.FindFirstValue("thumbnailUrl");

        try
        {
            var op = await _sync.SyncFromClaimsAsync(
                employeeCode, displayName, role, thumbnailUrl, primaryWarehouseId,
                context.RequestAborted);
            context.Items[OperatorIdItemKey] = op.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OperatorSyncMiddleware failed for {EmployeeCode}; request continuing without OperatorId.", employeeCode);
        }

        await next(context);
    }
}

public static class HttpContextOperatorExtensions
{
    // Convenience accessor for endpoints. Throws if middleware didn't
    // populate the key — endpoints calling this are inside
    // [Authorize(Policy = OperatorOnly)] so the sync MUST have run.
    public static Guid GetOperatorId(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(OperatorSyncMiddleware.OperatorIdItemKey, out var v) && v is Guid id)
            return id;
        throw new InvalidOperationException(
            "Operator Id not populated — OperatorSyncMiddleware did not run, or auth scheme is not OperatorJwt.");
    }
}
