using DTMS.SharedKernel.Operators;
using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Transport.Manual.Infrastructure.Services;

/// <summary>
/// Transport.Manual-side implementation of <see cref="IOperatorDirectory"/>.
/// Projects only <c>DisplayName</c> so cross-module callers (Dispatch's
/// trip-detail query) resolve the operator label without materializing the
/// full <c>Operator</c> aggregate.
/// </summary>
public sealed class OperatorDirectory : IOperatorDirectory
{
    private readonly TransportManualDbContext _db;
    public OperatorDirectory(TransportManualDbContext db) => _db = db;

    public async Task<string?> GetDisplayNameAsync(Guid operatorId, CancellationToken ct = default)
        => await _db.Operators
                    .Where(o => o.Id == operatorId)
                    .Select(o => o.DisplayName)
                    .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> operatorIds, CancellationToken ct = default)
    {
        if (operatorIds.Count == 0)
            return new Dictionary<Guid, string>();

        // Distinct so a page with many trips claimed by the same operator
        // sends one Id per operator, not one per row.
        var ids = operatorIds.Distinct().ToArray();
        return await _db.Operators
                        .Where(o => ids.Contains(o.Id))
                        .Select(o => new { o.Id, o.DisplayName })
                        .ToDictionaryAsync(o => o.Id, o => o.DisplayName, ct);
    }
}
