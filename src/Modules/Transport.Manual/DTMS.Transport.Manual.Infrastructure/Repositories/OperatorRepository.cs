using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Transport.Manual.Infrastructure.Repositories;

public sealed class OperatorRepository : IOperatorRepository
{
    private readonly TransportManualDbContext _db;
    public OperatorRepository(TransportManualDbContext db) => _db = db;

    public Task<Operator?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Operators.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Operator?> GetByEmployeeCodeAsync(string employeeCode, CancellationToken ct = default)
        => _db.Operators.FirstOrDefaultAsync(o => o.EmployeeCode == employeeCode, ct);

    public Task<Operator?> GetByEmployeeCodeWithDetailsAsync(string employeeCode, CancellationToken ct = default)
        => _db.Operators
              .Include(o => o.Certifications)
              .Include(o => o.PushSubscriptions)
              .FirstOrDefaultAsync(o => o.EmployeeCode == employeeCode, ct);

    public Task<Operator?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => _db.Operators
              .Include(o => o.Certifications)
              .Include(o => o.PushSubscriptions)
              .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Operator>> ListAllAsync(CancellationToken ct = default)
        => await _db.Operators
                    .OrderBy(o => o.Status)            // Active first (enum value 0)
                    .ThenBy(o => o.EmployeeCode)
                    .ToListAsync(ct);

    public async Task<IReadOnlyList<Operator>> GetEligibleForAssignmentAsync(
        CancellationToken ct = default)
    {
        // Any Active + idle operator qualifies — PR-3c dropped the
        // per-operator ServiceZones filter after ops confirmed the
        // in-plant Manual pool is fully mobile.
        return await _db.Operators
                       .Where(o => o.Status == OperatorStatus.Active
                                && o.CurrentTripId == null)
                       .OrderBy(o => o.EmployeeCode)
                       .ToListAsync(ct);
    }

    public Task AddAsync(Operator op, CancellationToken ct = default)
        => _db.Operators.AddAsync(op, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}

public sealed class GeofenceOverrideRequestRepository : IGeofenceOverrideRequestRepository
{
    private readonly TransportManualDbContext _db;
    public GeofenceOverrideRequestRepository(TransportManualDbContext db) => _db = db;

    public Task<GeofenceOverrideRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.GeofenceOverrideRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<GeofenceOverrideRequest?> GetApprovedForTripLegAsync(
        Guid tripId, Guid operatorId, Guid expectedWmsLocationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.GeofenceOverrideRequests
            .Where(r => r.TripId == tripId
                     && r.OperatorId == operatorId
                     && r.ExpectedWmsLocationId == expectedWmsLocationId
                     && r.Status == OverrideRequestStatus.Approved
                     && r.ExpiresAt > now)
            .OrderByDescending(r => r.DecidedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<GeofenceOverrideRequest>> ListPendingAsync(CancellationToken ct = default)
        => await _db.GeofenceOverrideRequests
                    .Where(r => r.Status == OverrideRequestStatus.Pending)
                    .OrderBy(r => r.RequestedAt)
                    .ToListAsync(ct);

    public Task AddAsync(GeofenceOverrideRequest request, CancellationToken ct = default)
        => _db.GeofenceOverrideRequests.AddAsync(request, ct).AsTask();

    public void Update(GeofenceOverrideRequest request)
        => _db.GeofenceOverrideRequests.Update(request);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}

public sealed class ManualTripExtensionRepository : IManualTripExtensionRepository
{
    private readonly TransportManualDbContext _db;
    public ManualTripExtensionRepository(TransportManualDbContext db) => _db = db;

    public Task<ManualTripExtension?> GetByTripIdAsync(Guid tripId, CancellationToken ct = default)
        => _db.ManualTripExtensions.FirstOrDefaultAsync(e => e.TripId == tripId, ct);

    public async Task<IReadOnlyList<ManualTripExtension>> GetByOperatorIdAsync(Guid operatorId, CancellationToken ct = default)
        => await _db.ManualTripExtensions
                    .Where(e => e.OperatorId == operatorId)
                    .OrderByDescending(e => e.AssignedAt)
                    .ToListAsync(ct);

    public async Task<IReadOnlyList<ManualTripExtension>> ListActiveAsync(CancellationToken ct = default)
        => await _db.ManualTripExtensions
                    .Where(e => e.DroppedAt == null)
                    .OrderByDescending(e => e.AssignedAt)
                    .ToListAsync(ct);

    public Task AddAsync(ManualTripExtension extension, CancellationToken ct = default)
        => _db.ManualTripExtensions.AddAsync(extension, ct).AsTask();

    public void Update(ManualTripExtension extension)
        => _db.ManualTripExtensions.Update(extension);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
