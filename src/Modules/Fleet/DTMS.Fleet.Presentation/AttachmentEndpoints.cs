using DTMS.Fleet.Application.Commands.ConfirmAttachment;
using DTMS.Fleet.Application.Commands.DeleteAttachment;
using DTMS.Fleet.Application.Commands.PresignAttachment;
using DTMS.Fleet.Application.Queries.GetAttachments;
using DTMS.Fleet.Domain.Entities;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Fleet.Presentation;

public record PresignAttachmentRequest(
    string Owner, Guid OwnerId, string ContentType, bool WithThumbnail = true);

public record ConfirmAttachmentRequest(
    string Owner, Guid OwnerId, Guid UploadId, bool WithThumbnail = true,
    string? OriginalFileName = null, string? Caption = null);

/// <summary>
/// Images attached to Fleet things (ADR-019).
///
/// <para><b>Permissions are the owner's own.</b> Whoever may read a carrier may
/// see its photos, and whoever may edit it may add and remove them. A separate
/// attachment permission would mean two checks guarding one decision, and would
/// let the two drift apart — someone able to edit a carrier but not, by
/// accident, to attach a photo to it.</para>
/// </summary>
public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/fleet/attachments")
            .WithTags("Fleet")
            .RequireAuthorization();

        // Read and write permissions differ per owner, and minimal APIs attach
        // them per route, so each verb is registered once per owner kind rather
        // than resolved at runtime. Verbose, but the permission for a route is
        // then visible in the route table instead of buried in a switch.
        foreach (var (owner, read, write) in OwnerPermissions())
        {
            var slug = OwnerSlug(owner);

            group.MapGet($"/{slug}/{{ownerId:guid}}", async (
                Guid ownerId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetAttachmentsQuery(owner, ownerId), ct);
                return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
            })
            .WithName($"ListFleetAttachments_{slug}")
            .RequirePermission(read);
        }

        // Presign and confirm name their owner in the body, so one route each
        // serves all three kinds — but that means the permission cannot be
        // attached per route. Both require write on the owner, checked inside.
        group.MapPost("/presign", async (
            [FromBody] PresignAttachmentRequest body, ISender sender, CancellationToken ct) =>
        {
            if (!TryParseOwner(body.Owner, out var owner))
                return Results.BadRequest($"Unknown owner '{body.Owner}'.");

            var result = await sender.Send(new PresignAttachmentCommand(
                owner, body.OwnerId, body.ContentType, body.WithThumbnail), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        })
        .WithName("PresignFleetAttachment")
        .WithSummary("Authorise one upload. Writes nothing until confirmed.")
        .RequirePermission(Permissions.Fleet.CarrierWrite);

        group.MapPost("/confirm", async (
            [FromBody] ConfirmAttachmentRequest body, ISender sender, CancellationToken ct) =>
        {
            if (!TryParseOwner(body.Owner, out var owner))
                return Results.BadRequest($"Unknown owner '{body.Owner}'.");

            var result = await sender.Send(new ConfirmAttachmentCommand(
                owner, body.OwnerId, body.UploadId, body.WithThumbnail,
                body.OriginalFileName, body.Caption), ct);

            return result.IsSuccess
                ? Results.Created($"/api/v1/fleet/attachments/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        })
        .WithName("ConfirmFleetAttachment")
        .WithSummary("Record a completed upload, taking its size and type from storage.")
        .RequirePermission(Permissions.Fleet.CarrierWrite);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteAttachmentCommand(id), ct);
            return result.IsSuccess ? Results.NoContent() : Results.NotFound(result.Error);
        })
        .WithName("DeleteFleetAttachment")
        .WithSummary("Remove an image; its bytes are cleared through the outbox.")
        .RequirePermission(Permissions.Fleet.CarrierWrite);
    }

    private static IEnumerable<(AttachmentOwner Owner, PermissionDefinition Read, PermissionDefinition Write)>
        OwnerPermissions() =>
    [
        (AttachmentOwner.Carrier, Permissions.Fleet.CarrierRead, Permissions.Fleet.CarrierWrite),
        (AttachmentOwner.CarrierType, Permissions.Fleet.CarrierTypeRead, Permissions.Fleet.CarrierTypeWrite),
        // A maintenance episode is part of a carrier's record, so it is gated
        // the same way the carrier is.
        (AttachmentOwner.MaintenanceLog, Permissions.Fleet.CarrierRead, Permissions.Fleet.CarrierWrite),
    ];

    private static string OwnerSlug(AttachmentOwner owner) => owner switch
    {
        AttachmentOwner.Carrier => "carrier",
        AttachmentOwner.CarrierType => "carrier-type",
        AttachmentOwner.MaintenanceLog => "maintenance",
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown attachment owner.")
    };

    private static bool TryParseOwner(string? value, out AttachmentOwner owner)
    {
        owner = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        foreach (var candidate in Enum.GetValues<AttachmentOwner>())
        {
            if (string.Equals(OwnerSlug(candidate), value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                owner = candidate;
                return true;
            }
        }
        return false;
    }
}
