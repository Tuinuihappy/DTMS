using DTMS.Fleet.Application.Commands.ConfirmAttachment;
using DTMS.Fleet.Application.Commands.DeleteAttachment;
using DTMS.Fleet.Application.Commands.PresignAttachment;
using DTMS.Fleet.Application.Queries.GetAttachments;
using DTMS.Fleet.Application.Queries.GetAttachmentImage;
using DTMS.Fleet.Domain.Entities;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Fleet.Presentation;

/// <summary>The owner comes from the route, so the body carries only the upload.</summary>
public record PresignAttachmentBody(string ContentType, bool WithThumbnail = true);

/// <summary>The owner comes from the route, so the body carries only the upload.</summary>
public record ConfirmAttachmentBody(
    Guid UploadId, bool WithThumbnail = true,
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
        // them per route, so every route is registered once per owner kind rather
        // than resolved at runtime. Verbose, but the permission for a route is
        // then visible in the route table instead of buried in a switch.
        //
        // Every route here names its owner in the path. Upload and delete used to
        // take the owner from the body or not at all, under one fixed CarrierWrite
        // check for every kind — so editing a carrier type's photos demanded
        // CarrierWrite, and CarrierWrite alone could delete them. Handlers that
        // receive an owner refuse an image that is not that owner's, which is what
        // keeps one kind's route from reaching another kind's pictures.
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

            // One image by owner and id — the thumbnail or the full picture —
            // answered with a redirect to a signed URL. It is meant for the
            // frontend relay, which fetches those bytes and serves them under a
            // stable address a browser can cache — image bytes for an id never
            // change.
            //
            // no-store on the redirect itself: the Location carries a signature
            // that expires in minutes, and a cached copy would outlive it.
            foreach (var (suffix, size) in ImageRoutes)
            {
                group.MapGet($"/{slug}/{{ownerId:guid}}/{{id:guid}}/{suffix}", async (
                    Guid ownerId, Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    var result = await sender.Send(new GetAttachmentImageQuery(owner, ownerId, id, size), ct);
                    return result.IsSuccess ? Results.Redirect(result.Value) : Results.NotFound(result.Error);
                })
                .WithName($"GetFleetAttachment_{size}_{slug}")
                .RequirePermission(read);
            }

            group.MapPost($"/{slug}/{{ownerId:guid}}/presign", async (
                Guid ownerId, [FromBody] PresignAttachmentBody body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new PresignAttachmentCommand(
                    owner, ownerId, body.ContentType, body.WithThumbnail), ct);
                return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
            })
            .WithName($"PresignFleetAttachment_{slug}")
            .WithSummary("Authorise one upload. Writes nothing until confirmed.")
            .RequirePermission(write);

            // Guarded by the owner being confirmed onto, not the one presigned
            // for. Staging keys are not tied to an owner, so this check is what
            // decides where the image may land.
            group.MapPost($"/{slug}/{{ownerId:guid}}/confirm", async (
                Guid ownerId, [FromBody] ConfirmAttachmentBody body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ConfirmAttachmentCommand(
                    owner, ownerId, body.UploadId, body.WithThumbnail,
                    body.OriginalFileName, body.Caption), ct);

                // No single-image GET exists, so the location is the owner's list.
                return result.IsSuccess
                    ? Results.Created($"/api/v1/fleet/attachments/{slug}/{ownerId}", result.Value)
                    : Results.BadRequest(result.Error);
            })
            .WithName($"ConfirmFleetAttachment_{slug}")
            .WithSummary("Record a completed upload, taking its size and type from storage.")
            .RequirePermission(write);

            group.MapDelete($"/{slug}/{{ownerId:guid}}/{{id:guid}}", async (
                Guid ownerId, Guid id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteAttachmentCommand(owner, ownerId, id), ct);
                return result.IsSuccess ? Results.NoContent() : Results.NotFound(result.Error);
            })
            .WithName($"DeleteFleetAttachment_{slug}")
            .WithSummary("Remove an image; its bytes are cleared through the outbox.")
            .RequirePermission(write);
        }
    }

    // The last path segment of each image route. Program.cs gives these paths a
    // rate-limit bucket of their own by the same suffixes — keep the two in step.
    private static readonly (string Suffix, AttachmentImageSize Size)[] ImageRoutes =
    [
        ("thumbnail", AttachmentImageSize.Thumbnail),
        ("image", AttachmentImageSize.Full),
    ];

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
}
