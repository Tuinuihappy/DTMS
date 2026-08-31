using DTMS.Fleet.Application.Commands.DeleteCarrier;
using DTMS.Fleet.Application.Commands.MoveCarrier;
using DTMS.Fleet.Application.Commands.RegisterCarrier;
using DTMS.Fleet.Application.Commands.RegisterCarrierType;
using DTMS.Fleet.Application.Commands.RetireCarrier;
using DTMS.Fleet.Application.Commands.ReturnCarrierToService;
using DTMS.Fleet.Application.Commands.SetCarrierMaintenance;
using DTMS.Fleet.Application.Commands.UnretireCarrier;
using DTMS.Fleet.Application.Commands.UpdateCarrier;
using DTMS.Fleet.Application.Queries.GetCarrierByCode;
using DTMS.Fleet.Application.Queries.GetCarrierMaintenanceHistory;
using DTMS.Fleet.Application.Queries.GetCarriers;
using DTMS.Fleet.Application.Queries.GetCarrierTypes;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Fleet.Presentation;

public record UpdateCarrierRequest(
    string CarrierTypeCode, string? Barcode = null, string? DisplayName = null,
    DateTime? CommissionedAt = null);
public record MoveCarrierRequest(string? CurrentLocationCode);
public record SetCarrierMaintenanceRequest(string Reason);
public record ReturnCarrierToServiceRequest(string? Outcome = null);
public record RetireCarrierRequest(string Reason);

/// <summary>
/// Carrier endpoints (ADR-019). Separate from VehicleEndpoints because
/// carriers are a distinct asset class that shares nothing with the vendor
/// vehicle surface — no RIOT3 identity, no import, no state webhook.
/// </summary>
public static class CarrierEndpoints
{
    public static void MapCarrierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/fleet").WithTags("Fleet").RequireAuthorization();

        // ── Carrier Types ──────────────────────────────────────────────────
        group.MapPost("/carrier-types", async (RegisterCarrierTypeCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/fleet/carrier-types/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierTypeWrite);

        group.MapGet("/carrier-types", async (ISender sender) =>
        {
            var result = await sender.Send(new GetCarrierTypesQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierTypeRead);

        // ── Carriers ───────────────────────────────────────────────────────
        // {code} is the natural key operators read off the cart, so it is what
        // the routes address. Carrier.NormalizeAndValidateCode restricts the
        // charset precisely because the value lands in a URL path segment.
        group.MapPost("/carriers", async (RegisterCarrierCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/fleet/carriers/{command.CarrierCode.Trim().ToUpperInvariant()}", result.Value)
                : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierWrite);

        group.MapGet("/carriers", async (
            string? status, string? carrierTypeCode, string? q, int? page, int? pageSize, ISender sender) =>
        {
            var result = await sender.Send(new GetCarriersQuery(
                status, carrierTypeCode, q,
                page ?? 1,
                pageSize ?? GetCarriersQuery.DefaultPageSize));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierRead);

        group.MapGet("/carriers/{code}", async (string code, ISender sender) =>
        {
            var result = await sender.Send(new GetCarrierByCodeQuery(code));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierRead);

        group.MapPut("/carriers/{code}", async (
            string code, [FromBody] UpdateCarrierRequest body, ISender sender) =>
        {
            var result = await sender.Send(new UpdateCarrierCommand(
                code, body.CarrierTypeCode, body.Barcode, body.DisplayName, body.CommissionedAt));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierWrite);

        group.MapPut("/carriers/{code}/location", async (
            string code, [FromBody] MoveCarrierRequest body, ISender sender) =>
        {
            var result = await sender.Send(new MoveCarrierCommand(code, body.CurrentLocationCode));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierWrite);

        // ── Lifecycle transitions ──────────────────────────────────────────
        // All POST. DELETE is reserved for the one operation that actually
        // destroys a row — otherwise "bring this carrier back" and "erase this
        // carrier" would be the same verb one path segment apart, and dropping
        // the segment by accident would be unrecoverable.
        group.MapPost("/carriers/{code}/maintenance", async (
            string code, [FromBody] SetCarrierMaintenanceRequest body, ISender sender) =>
        {
            var result = await sender.Send(new SetCarrierMaintenanceCommand(code, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierMaintain);

        group.MapPost("/carriers/{code}/return-to-service", async (
            string code, [FromBody] ReturnCarrierToServiceRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ReturnCarrierToServiceCommand(code, body?.Outcome));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierMaintain);

        group.MapGet("/carriers/{code}/maintenance", async (string code, ISender sender) =>
        {
            var result = await sender.Send(new GetCarrierMaintenanceHistoryQuery(code));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierRead);

        group.MapPost("/carriers/{code}/retire", async (
            string code, [FromBody] RetireCarrierRequest body, ISender sender) =>
        {
            var result = await sender.Send(new RetireCarrierCommand(code, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierWrite);

        group.MapPost("/carriers/{code}/unretire", async (string code, ISender sender) =>
        {
            var result = await sender.Send(new UnretireCarrierCommand(code));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Fleet.CarrierWrite);

        // The only DELETE. Refusal carries the domain's own reason so the caller
        // is told what is blocking and pointed at retire instead.
        group.MapDelete("/carriers/{code}", async (string code, ISender sender) =>
        {
            var result = await sender.Send(new DeleteCarrierCommand(code));
            if (!result.IsSuccess) return Results.BadRequest(result.Error);

            return result.Value.Outcome switch
            {
                DeleteCarrierOutcome.Deleted => Results.NoContent(),
                DeleteCarrierOutcome.NotFound => Results.NotFound(result.Value.Reason),
                _ => Results.Conflict(result.Value.Reason)
            };
        }).RequirePermission(Permissions.Fleet.CarrierDelete);
    }
}
