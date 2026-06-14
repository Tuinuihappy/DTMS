using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Projections;

public class TripFactsProjectionStore : ITripFactsProjectionStore
{
    private readonly DispatchDbContext _db;

    public TripFactsProjectionStore(DispatchDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, ct);

    public async Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureRowAsync(
        Guid tripId, DateTime occurredAt,
        Guid? deliveryOrderId, Guid? jobId, CancellationToken ct)
    {
        var row = await _db.TripFacts.FirstOrDefaultAsync(r => r.TripId == tripId, ct);
        if (row is null)
        {
            // No TripCreated event exists — first event we see is the row's
            // birthday. Backfill SQL overrides CreatedAt for pre-P5.2 trips.
            _db.TripFacts.Add(TripFactsRow.Create(
                tripId, occurredAt, deliveryOrderId, jobId, finalStatus: "Created"));
        }
    }

    public async Task SetStartedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId, Guid? vehicleId,
        string? vendorVehicleKey, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetStartedAt(at, deliveryOrderId, jobId, vehicleId, vendorVehicleKey);
    }

    public async Task RecordPausedAsync(Guid tripId, DateTime at, CancellationToken ct)
        => (await Find(tripId, ct))?.RecordPaused(at);

    public async Task RecordResumedAsync(Guid tripId, DateTime at, CancellationToken ct)
        => (await Find(tripId, ct))?.RecordResumed(at);

    public async Task SetCompletedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId, string? vendorUpperKey, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetCompletedAt(at, deliveryOrderId, jobId, vendorUpperKey);
    }

    public async Task SetFailedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetFailedAt(at, deliveryOrderId, jobId, vendorUpperKey, reason);
    }

    public async Task SetCancelledAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct)
    {
        await EnsureRowAsync(tripId, at, deliveryOrderId, jobId, ct);
        var row = await Find(tripId, ct);
        row?.SetCancelledAt(at, deliveryOrderId, jobId, vendorUpperKey, reason);
    }

    private Task<TripFactsRow?> Find(Guid tripId, CancellationToken ct)
        => _db.TripFacts.FirstOrDefaultAsync(r => r.TripId == tripId, ct);
}
