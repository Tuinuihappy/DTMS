using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;

namespace DTMS.Transport.Manual.Application.Services;

// Phase 4.4 — Policy that picks which operator gets a Manual trip.
//
// MVP rules (this implementation; the interface lets us swap in a
// smarter scheduler later without touching ManualDispatchStrategy):
//   1. Operator must be Active + idle (CurrentTripId IS NULL) — enforced
//      at the repository level via GetEligibleForAssignmentAsync.
//   2. If RequiredCertifications is non-empty, operator must hold ALL of
//      them (active + not-expired).
//   3. Prefer operators whose PrimaryWarehouseId matches the pickup
//      warehouse — repo orders these first.
//   4. Among equally-eligible candidates pick the first (lexical
//      EmployeeCode) for deterministic behaviour.
//
// Out of scope for 4.4 — would benefit from a real scheduler in a
// future phase:
//   - Cargo weight / size matching operator vehicle capacity
//   - Time-of-day shift scoping (OperatorShift entity exists but isn't
//     populated yet)
//   - Load balancing across operators (round-robin / least recently used)
//   - Proximity (current GPS vs warehouse) for nearest-operator picking
public interface IOperatorAssignmentPolicy
{
    Task<OperatorAssignmentResult> SelectOperatorAsync(
        OperatorAssignmentContext context,
        CancellationToken ct = default);
}

public sealed record OperatorAssignmentContext(
    Guid? PickupWarehouseId,
    IReadOnlyList<CertificationType> RequiredCertifications);

public sealed record OperatorAssignmentResult(
    Operator? Operator,
    string? RejectionReason)
{
    public bool IsAssigned => Operator is not null;
    public static OperatorAssignmentResult Assigned(Operator op) => new(op, null);
    public static OperatorAssignmentResult NoMatch(string reason) => new(null, reason);
}
