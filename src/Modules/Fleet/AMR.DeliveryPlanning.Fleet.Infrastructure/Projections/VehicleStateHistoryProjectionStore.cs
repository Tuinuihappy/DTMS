using AMR.DeliveryPlanning.Fleet.Application.Projections;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Projections;

public class VehicleStateHistoryProjectionStore : IVehicleStateHistoryProjectionStore
{
    private readonly FleetDbContext _db;

    public VehicleStateHistoryProjectionStore(FleetDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, cancellationToken);

    public async Task<(string ToState, DateTime OccurredAt)?> GetLatestForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        var row = await _db.VehicleStateHistory
            .AsNoTracking()
            .Where(r => r.VehicleId == vehicleId)
            .OrderByDescending(r => r.OccurredAt)
            .Select(r => new { r.ToState, r.OccurredAt })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : (row.ToState, row.OccurredAt);
    }

    public async Task AppendAsync(
        string projectorName, Guid eventId, Guid vehicleId,
        string? fromState, string toState,
        double batteryLevel, Guid? currentNodeId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        _db.VehicleStateHistory.Add(new VehicleStateHistoryRow(
            eventId, vehicleId, fromState, toState,
            batteryLevel, currentNodeId, occurredAt));
        _db.ProjectionInbox.Add(
            new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
