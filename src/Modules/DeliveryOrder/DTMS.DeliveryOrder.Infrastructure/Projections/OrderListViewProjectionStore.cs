using System.Diagnostics;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

public class OrderListViewProjectionStore : IOrderListViewProjectionStore
{
    private readonly DeliveryOrderDbContext _db;

    public OrderListViewProjectionStore(DeliveryOrderDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTripDerivedFieldsAsync(Guid orderId, bool hasFailedTrip, Guid? latestTripId, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null) return;
        row.SetTripDerivedFields(hasFailedTrip, latestTripId);
    }

    public async Task SetJobDerivedFieldsAsync(Guid orderId, bool hasActiveJob, string? latestJobStatus, CancellationToken cancellationToken = default)
    {
        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null) return;
        row.SetJobDerivedFields(hasActiveJob, latestJobStatus);
    }

    public async Task RefreshFromAggregateAsync(Guid orderId, DateTime occurredAt, CancellationToken cancellationToken = default)
    {
        var order = await _db.DeliveryOrders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        // Aggregate gone (race with delete) — nothing to project.
        if (order is null) return;

        var search = BuildSearchText(order);
        var status = order.Status.ToString();
        var sourceSystem = order.SourceSystem.ToString();
        var priority = order.Priority.ToString();
        var transportMode = order.RequestedTransportMode?.ToString();
        var swEarliest = order.ServiceWindow?.EarliestUtc;
        var swLatest = order.ServiceWindow?.LatestUtc;

        var row = await _db.OrderListView.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
        if (row is null)
        {
            _db.OrderListView.Add(new OrderListViewRow(
                order.Id, order.OrderRef, status, sourceSystem, priority, transportMode,
                order.RequestedBy, order.CreatedBy, order.Notes,
                order.TotalItems, order.TotalQuantity, order.TotalWeightKg,
                order.RequiresDropPod, order.RequiresPickupPod,
                order.CreatedDate, occurredAt, order.SubmittedAt,
                swEarliest, swLatest,
                search));
            return;
        }

        row.RefreshFromAggregate(
            status, sourceSystem, priority, transportMode,
            order.RequestedBy, order.CreatedBy, order.Notes,
            order.TotalItems, order.TotalQuantity, order.TotalWeightKg,
            order.RequiresDropPod, order.RequiresPickupPod,
            order.SubmittedAt, swEarliest, swLatest,
            search, occurredAt);
    }

    private static string BuildSearchText(Domain.Entities.DeliveryOrder order)
    {
        var itemText = string.Join(' ',
            order.Items.Select(i => i.ItemId).Where(s => !string.IsNullOrEmpty(s)));
        return string.Join(' ', new[]
        {
            order.Id.ToString("N"),
            order.OrderRef,
            itemText,
        }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public async Task<ProjectionRebuildResult> RebuildAllAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            var upserted = await _db.Database.ExecuteSqlRawAsync(UpsertSql, cancellationToken);
            var deleted = await _db.Database.ExecuteSqlRawAsync(DeleteOrphansSql, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            sw.Stop();
            return new ProjectionRebuildResult(upserted, deleted, sw.Elapsed);
        });
    }

    // Idempotent UPSERT — derives every projection column from the canonical
    // sources. Trip/Job derived fields are recomputed from "latest by
    // CreatedAt" because the live projector treats those events as
    // last-write-wins (TripStarted clears HasFailedTrip set by an earlier
    // TripFailed). ON CONFLICT updates ALL columns except CreatedAt
    // (immutable). SearchText matches BuildSearchText above byte-for-byte
    // so a rebuild row is indistinguishable from one written by the live
    // projector.
    private const string UpsertSql = """
        INSERT INTO deliveryorder."OrderListView" (
            "OrderId", "OrderRef", "Status", "SourceSystem", "Priority", "TransportMode",
            "HasFailedTrip", "HasActiveJob", "LatestTripId", "LatestJobStatus",
            "RequestedBy", "CreatedBy", "Notes",
            "TotalItems", "TotalQuantity", "TotalWeightKg",
            "RequiresDropPod", "RequiresPickupPod",
            "CreatedAt", "UpdatedAt", "SubmittedAt",
            "ServiceWindowEarliestUtc", "ServiceWindowLatestUtc",
            "SearchText"
        )
        SELECT
            o."Id",
            o."OrderRef",
            o."Status",
            o."SourceSystem",
            o."Priority",
            o."RequestedTransportMode",
            COALESCE(lt."TripStatus" IN ('Failed','Cancelled'), false),
            COALESCE(lj."JobStatus" IN ('Created','Assigned','Committed','Dispatched','Executing'), false),
            lt."LatestTripId",
            lj."JobStatus",
            o."RequestedBy",
            o."CreatedBy",
            o."Notes",
            o."TotalItems",
            o."TotalQuantity",
            o."TotalWeightKg",
            o."RequiresDropPod",
            o."RequiresPickupPod",
            o."CreatedDate",
            NOW() AT TIME ZONE 'UTC',
            o."SubmittedAt",
            o."ServiceWindow_EarliestUtc",
            o."ServiceWindow_LatestUtc",
            CONCAT_WS(' ',
                REPLACE(o."Id"::text, '-', ''),
                NULLIF(o."OrderRef", ''),
                (SELECT STRING_AGG(i."ItemId", ' ')
                 FROM deliveryorder."Items" i
                 WHERE i."DeliveryOrderId" = o."Id" AND i."ItemId" IS NOT NULL AND i."ItemId" <> '')
            )
        FROM deliveryorder."DeliveryOrders" o
        LEFT JOIN LATERAL (
            SELECT t."Id" AS "LatestTripId", t."Status" AS "TripStatus"
            FROM dispatch."Trips" t
            WHERE t."DeliveryOrderId" = o."Id"
            ORDER BY t."CreatedAt" DESC
            LIMIT 1
        ) lt ON TRUE
        LEFT JOIN LATERAL (
            SELECT j."Status" AS "JobStatus"
            FROM planning."Jobs" j
            WHERE j."DeliveryOrderId" = o."Id"
            ORDER BY j."CreatedAt" DESC
            LIMIT 1
        ) lj ON TRUE
        ON CONFLICT ("OrderId") DO UPDATE SET
            "OrderRef" = EXCLUDED."OrderRef",
            "Status" = EXCLUDED."Status",
            "SourceSystem" = EXCLUDED."SourceSystem",
            "Priority" = EXCLUDED."Priority",
            "TransportMode" = EXCLUDED."TransportMode",
            "HasFailedTrip" = EXCLUDED."HasFailedTrip",
            "HasActiveJob" = EXCLUDED."HasActiveJob",
            "LatestTripId" = EXCLUDED."LatestTripId",
            "LatestJobStatus" = EXCLUDED."LatestJobStatus",
            "RequestedBy" = EXCLUDED."RequestedBy",
            "CreatedBy" = EXCLUDED."CreatedBy",
            "Notes" = EXCLUDED."Notes",
            "TotalItems" = EXCLUDED."TotalItems",
            "TotalQuantity" = EXCLUDED."TotalQuantity",
            "TotalWeightKg" = EXCLUDED."TotalWeightKg",
            "RequiresDropPod" = EXCLUDED."RequiresDropPod",
            "RequiresPickupPod" = EXCLUDED."RequiresPickupPod",
            "SubmittedAt" = EXCLUDED."SubmittedAt",
            "ServiceWindowEarliestUtc" = EXCLUDED."ServiceWindowEarliestUtc",
            "ServiceWindowLatestUtc" = EXCLUDED."ServiceWindowLatestUtc",
            "SearchText" = EXCLUDED."SearchText",
            "UpdatedAt" = EXCLUDED."UpdatedAt";
        """;

    // Drop projection rows whose aggregate no longer exists (deleted out
    // of band — should be rare but keeps the projection lean).
    private const string DeleteOrphansSql = """
        DELETE FROM deliveryorder."OrderListView" v
        WHERE NOT EXISTS (
            SELECT 1 FROM deliveryorder."DeliveryOrders" o WHERE o."Id" = v."OrderId"
        );
        """;
}
