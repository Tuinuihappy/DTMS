using AMR.DeliveryPlanning.Planning.Application.Projections;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Projections;

public class JobFactsProjectionStore : IJobFactsProjectionStore
{
    private readonly PlanningDbContext _db;

    public JobFactsProjectionStore(PlanningDbContext db) => _db = db;

    public Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct)
        => _db.ProjectionInbox
            .AsNoTracking()
            .AnyAsync(m => m.ProjectorName == projectorName && m.EventId == eventId, ct);

    public async Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct)
    {
        _db.ProjectionInbox.Add(new InboxMessage(projectorName, eventId, DateTime.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpsertOnCreatedAsync(
        Guid jobId, Guid deliveryOrderId, DateTime createdAt, CancellationToken ct)
    {
        var row = await Find(jobId, ct);
        if (row is null)
        {
            _db.JobFacts.Add(JobFactsRow.Create(
                jobId, deliveryOrderId, createdAt, "Created"));
        }
    }

    public async Task SetAssignedAtAsync(Guid jobId, DateTime at, Guid? vehicleId, CancellationToken ct)
        => (await Find(jobId, ct))?.SetAssignedAt(at, vehicleId);

    public async Task SetCommittedAtAsync(Guid jobId, DateTime at, Guid? vehicleId, CancellationToken ct)
        => (await Find(jobId, ct))?.SetCommittedAt(at, vehicleId);

    public async Task SetDispatchedAtAsync(
        Guid jobId, DateTime at, Guid? tripId, string? vendorOrderKey,
        int attemptNumber, CancellationToken ct)
        => (await Find(jobId, ct))?.SetDispatchedAt(at, tripId, vendorOrderKey, attemptNumber);

    public async Task SetExecutingAtAsync(Guid jobId, DateTime at, Guid? tripId, CancellationToken ct)
        => (await Find(jobId, ct))?.SetExecutingAt(at, tripId);

    public async Task SetCompletedAtAsync(Guid jobId, DateTime at, Guid? tripId, CancellationToken ct)
        => (await Find(jobId, ct))?.SetCompletedAt(at, tripId);

    public async Task SetFailedAtAsync(
        Guid jobId, DateTime at, string? reason, int attemptNumber,
        string? failureCategory, CancellationToken ct)
        => (await Find(jobId, ct))?.SetFailedAt(at, reason, attemptNumber, failureCategory);

    public async Task SetCancelledAtAsync(
        Guid jobId, DateTime at, Guid? tripId, string? reason,
        string? failureCategory, CancellationToken ct)
        => (await Find(jobId, ct))?.SetCancelledAt(at, tripId, reason, failureCategory);

    private Task<JobFactsRow?> Find(Guid jobId, CancellationToken ct)
        => _db.JobFacts.FirstOrDefaultAsync(r => r.JobId == jobId, ct);
}
