using DTMS.SharedKernel.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Api.Modules;

/// <summary>
/// Phase O3 — admin surface for the outbox Dead Letter Queue. Reads and
/// mutates <c>outbox.DeadLetterMessages</c> via <see cref="IDeadLetterStore"/>.
///
/// <para>Auth mirrors <c>AdminProjectionsEndpoints</c>: MapGroup under
/// <c>/api/v1/admin</c> with <c>RequireAuthorization()</c>. Ops-only.</para>
/// </summary>
public static class AdminOutboxEndpoints
{
    public static void MapAdminOutboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/outbox/dlq")
                       .WithTags("Admin")
                       .RequireAuthorization();

        // GET /api/v1/admin/outbox/dlq?take=50&skip=0&source=deliveryorder
        // Newest-failure-first. `source` filter is optional (module slug).
        group.MapGet("/", async (
            IDeadLetterStore store,
            CancellationToken ct,
            [FromQuery] int take = 50,
            [FromQuery] int skip = 0,
            [FromQuery] string? source = null) =>
        {
            var rows = await store.ListAsync(take, skip, source, ct);
            var total = await store.CountAsync(ct);
            return Results.Ok(new
            {
                total,
                take,
                skip,
                items = rows.Select(m => new
                {
                    id = m.Id,
                    originalOutboxId = m.OriginalOutboxId,
                    source = m.Source,
                    type = m.Type,
                    occurredOnUtc = m.OccurredOnUtc,
                    firstFailedOnUtc = m.FirstFailedOnUtc,
                    lastFailedOnUtc = m.LastFailedOnUtc,
                    retryCount = m.RetryCount,
                    lastError = m.LastError,
                    traceParent = m.TraceParent,
                    // Content deliberately excluded from the list view to
                    // keep response small. Use GET /{id} to inspect payload.
                }),
            });
        });

        // GET /api/v1/admin/outbox/dlq/{id} — full row including Content
        group.MapGet("/{id:guid}", async (Guid id, IDeadLetterStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        // POST /api/v1/admin/outbox/dlq/{id}/replay
        // Re-emits the DLQ message into its origin module's OutboxMessages
        // and deletes the DLQ row on success. Idempotent — a second call
        // for a DLQ id that was already replayed returns 404.
        group.MapPost("/{id:guid}/replay", async (Guid id, IDeadLetterStore store, CancellationToken ct) =>
        {
            try
            {
                var ok = await store.ReplayAsync(id, ct);
                return ok ? Results.Ok(new { id, replayed = true })
                          : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                // Router rejected an unknown Source — 500 with the message.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        // DELETE /api/v1/admin/outbox/dlq/{id} — permanent drop, no replay
        group.MapDelete("/{id:guid}", async (Guid id, IDeadLetterStore store, CancellationToken ct) =>
        {
            var ok = await store.DeleteAsync(id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });
    }
}
