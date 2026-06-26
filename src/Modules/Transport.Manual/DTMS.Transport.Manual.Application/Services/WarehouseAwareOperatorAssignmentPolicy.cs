using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Services;

// Phase 4.4 — MVP implementation of IOperatorAssignmentPolicy. See the
// interface docs for the rule list.
public sealed class WarehouseAwareOperatorAssignmentPolicy : IOperatorAssignmentPolicy
{
    private readonly IOperatorRepository _operators;

    public WarehouseAwareOperatorAssignmentPolicy(IOperatorRepository operators)
        => _operators = operators;

    public async Task<OperatorAssignmentResult> SelectOperatorAsync(
        OperatorAssignmentContext context,
        CancellationToken ct = default)
    {
        var candidates = await _operators.GetEligibleForAssignmentAsync(
            context.PickupWarehouseId, ct);

        if (candidates.Count == 0)
        {
            return OperatorAssignmentResult.NoMatch(
                context.PickupWarehouseId.HasValue
                    ? $"No active + idle operator found (preferred warehouse {context.PickupWarehouseId})."
                    : "No active + idle operator found.");
        }

        // Cert filter — operator must hold every required cert, currently
        // valid as of now. Empty required list short-circuits to "first
        // candidate wins" without loading details.
        if (context.RequiredCertifications.Count == 0)
        {
            return OperatorAssignmentResult.Assigned(candidates[0]);
        }

        var now = DateTime.UtcNow;
        foreach (var candidate in candidates)
        {
            // Repo returns operators without their navigation collections —
            // fetch the details for any candidate we'd consider. In
            // practice the first warehouse-matched candidate usually
            // qualifies, so this is N=1 reads, not N=many.
            var detailed = await _operators.GetByIdWithDetailsAsync(candidate.Id, ct);
            if (detailed is null) continue;
            var hasAllCerts = context.RequiredCertifications.All(req =>
                detailed.Certifications.Any(c => c.Type == req && c.IsCurrentlyValid(now)));
            if (hasAllCerts)
                return OperatorAssignmentResult.Assigned(detailed);
        }

        return OperatorAssignmentResult.NoMatch(
            $"No eligible operator holds the required certifications " +
            $"({string.Join(", ", context.RequiredCertifications)}).");
    }
}
