using DTMS.Api.Realtime.Hubs;
using DTMS.Api.Realtime.Hubs.Clients;
using DTMS.Transport.Manual.Application.Commands.Admin.ApproveGeofenceOverride;
using DTMS.Transport.Manual.Application.Commands.Admin.DenyGeofenceOverride;
using DTMS.Transport.Manual.Application.Commands.Admin.ReassignManualTrip;
using DTMS.Transport.Manual.Application.Queries.Admin;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Modules;

// Phase 4.6 — Dispatcher console admin surface for Manual mode.
// Mounted under /api/v1/admin to share the existing admin auth policy
// (RequireAuthorization with the primary JWT scheme — admins use the
// same login flow as the operator PWA, just with role=Admin in the
// JWT, so the same cookie reaches here).
//
// Endpoints:
//   GET    /api/v1/admin/manual/operators              — operator board
//   GET    /api/v1/admin/manual/trips                  — active Manual trips
//   GET    /api/v1/admin/manual/geofence-overrides     — pending review queue
//   POST   /api/v1/admin/manual/geofence-overrides/{id}/approve
//   POST   /api/v1/admin/manual/geofence-overrides/{id}/deny
//   POST   /api/v1/admin/manual/trips/{tripId}/reassign
//
// decidedByOperatorId / actor inputs are supplied by the dispatcher
// console from its own /api/operator/me lookup — supervisors+admins
// have Operator rows per ADR-014, and the frontend already knows the
// Id at the time of decision.
public static class AdminManualOperatorEndpoints
{
    public static void MapAdminManualOperatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/manual")
            .WithTags("Admin", "Manual")
            .RequireAuthorization();

        group.MapGet("/operators", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListOperatorsQuery(), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(result.Error);
        })
        .WithName("AdminListManualOperators")
        .WithSummary("List all Manual-mode operators (active, on-leave, deactivated).");

        group.MapGet("/trips", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListManualTripsQuery(), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(result.Error);
        })
        .WithName("AdminListManualTrips")
        .WithSummary("List active Manual trips (not yet dropped).");

        group.MapGet("/geofence-overrides", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListPendingOverridesQuery(), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(result.Error);
        })
        .WithName("AdminListPendingOverrides")
        .WithSummary("List pending geofence-override requests awaiting approval.");

        group.MapPost("/geofence-overrides/{id:guid}/approve",
            async (Guid id, [FromBody] OverrideApproveRequest body, ISender sender,
                   IHubContext<ManualBoardHub, IManualBoardClient> board, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveGeofenceOverrideCommand(
                OverrideRequestId: id,
                DecidedByOperatorId: body.DecidedByOperatorId,
                Note: body.Note), ct);
            if (!result.IsSuccess) return Results.BadRequest(new { Error = result.Error });
            await board.Clients.Group(ManualBoardHub.BoardGroup).OverrideDecided(
                new { OverrideRequestId = id, Status = "Approved" });
            return Results.NoContent();
        })
        .WithName("AdminApproveGeofenceOverride")
        .WithSummary("Approve a pending geofence-override request.");

        group.MapPost("/geofence-overrides/{id:guid}/deny",
            async (Guid id, [FromBody] OverrideDenyRequest body, ISender sender,
                   IHubContext<ManualBoardHub, IManualBoardClient> board, CancellationToken ct) =>
        {
            var result = await sender.Send(new DenyGeofenceOverrideCommand(
                OverrideRequestId: id,
                DecidedByOperatorId: body.DecidedByOperatorId,
                Reason: body.Reason), ct);
            if (!result.IsSuccess) return Results.BadRequest(new { Error = result.Error });
            await board.Clients.Group(ManualBoardHub.BoardGroup).OverrideDecided(
                new { OverrideRequestId = id, Status = "Denied" });
            return Results.NoContent();
        })
        .WithName("AdminDenyGeofenceOverride")
        .WithSummary("Deny a pending geofence-override request.");

        group.MapPost("/trips/{tripId:guid}/reassign",
            async (Guid tripId, [FromBody] ReassignRequest body, ISender sender,
                   IHubContext<ManualBoardHub, IManualBoardClient> board, CancellationToken ct) =>
        {
            var result = await sender.Send(new ReassignManualTripCommand(
                TripId: tripId,
                NewOperatorId: body.NewOperatorId,
                Reason: body.Reason), ct);
            if (!result.IsSuccess) return Results.BadRequest(new { Error = result.Error });
            await board.Clients.Group(ManualBoardHub.BoardGroup).TripReassigned(
                new { TripId = tripId, NewOperatorId = body.NewOperatorId });
            return Results.NoContent();
        })
        .WithName("AdminReassignManualTrip")
        .WithSummary("Move an active Manual trip to a different operator.");
    }
}

public sealed record OverrideApproveRequest(Guid DecidedByOperatorId, string? Note);
public sealed record OverrideDenyRequest(Guid DecidedByOperatorId, string Reason);
public sealed record ReassignRequest(Guid NewOperatorId, string? Reason);
