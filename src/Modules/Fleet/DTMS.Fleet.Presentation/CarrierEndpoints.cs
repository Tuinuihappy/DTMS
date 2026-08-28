using DTMS.Fleet.Application.Commands.RegisterCarrierType;
using DTMS.Fleet.Application.Queries.GetCarrierTypes;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Fleet.Presentation;

/// <summary>
/// Carrier endpoints (ADR-019). Separate from VehicleEndpoints because
/// carriers are a distinct asset class that shares nothing with the vendor
/// vehicle surface — no RIOT3 identity, no import, no state webhook.
///
/// <para>Permission note: these still carry <c>Permissions.Facility.Profile*</c>
/// while the routes move. The rename to <c>dtms:fleet:carrier-type:*</c> is a
/// separate commit (P0.2b), held back so the schema move can ship without
/// waiting on grants being updated on the external auth service — user
/// permissions ride inline in the LDAP JWT, so a rename only takes effect
/// once affected users obtain a fresh token.</para>
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
        }).RequirePermission(Permissions.Facility.ProfileWrite);

        group.MapGet("/carrier-types", async (ISender sender) =>
        {
            var result = await sender.Send(new GetCarrierTypesQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Facility.ProfileRead);
    }
}
