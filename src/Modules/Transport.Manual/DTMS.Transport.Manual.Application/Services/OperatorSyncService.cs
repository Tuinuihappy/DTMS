using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Services;

public sealed class OperatorSyncService : IOperatorSyncService
{
    private readonly IOperatorRepository _repo;
    public OperatorSyncService(IOperatorRepository repo) => _repo = repo;

    public async Task<Operator> SyncFromClaimsAsync(
        string employeeCode,
        string displayName,
        OperatorRole role,
        string? thumbnailUrl,
        CancellationToken ct = default)
    {
        var existing = await _repo.GetByEmployeeCodeAsync(employeeCode, ct);
        if (existing is null)
        {
            // First-time login — create the DTMS-side row. Domain
            // factory raises OperatorRegisteredDomainEvent. ServiceZones
            // are seeded empty; admin (PR-4 UI or seed script) populates
            // them before the operator can receive Manual trips.
            var created = Operator.CreateFromJwtClaims(
                employeeCode, displayName, role,
                phone: null,
                thumbnailUrl: thumbnailUrl);
            await _repo.AddAsync(created, ct);
            await _repo.SaveChangesAsync(ct);
            return created;
        }

        // Subsequent logins — overwrite DisplayName + Role (External
        // Auth owns those). ServiceZones stay under DTMS's own admin
        // control; External Auth doesn't own them.
        var nameDrifted = existing.DisplayName != displayName.Trim();
        var roleDrifted = existing.Role != role;
        if (nameDrifted || roleDrifted)
        {
            existing.SyncFromJwtClaims(displayName, role, thumbnailUrl);
            await _repo.SaveChangesAsync(ct);
        }
        return existing;
    }
}
