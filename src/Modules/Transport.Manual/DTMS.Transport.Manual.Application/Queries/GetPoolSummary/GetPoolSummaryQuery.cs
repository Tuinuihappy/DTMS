using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Queries.GetPoolSummary;

// WMS PR-4b (PR-G) — Dispatcher pool health snapshot. One-shot query the
// admin/dispatcher board polls (or subscribes to via ManualBoardHub).
// Returns aggregate numbers only — the per-trip list still comes from
// GetPoolTripsQuery.
public record GetPoolSummaryQuery : IQuery<PoolSummaryDto>;

// Wire shape sized for the dispatcher summary card. All fields are
// derivable from the same partial-index scan as GetPoolTripsQuery so the
// two are cheap to run side by side.
public sealed record PoolSummaryDto(
    // How many trips are in the pool right now (Status=Created ∧
    // DispatchedAt IS NOT NULL ∧ ClaimedByOperatorId IS NULL).
    int PoolDepth,

    // Waited time of the oldest pool trip. Alerts fire when this exceeds
    // the ops SLA (~5 min in the current setup). Null when the pool is empty.
    double? OldestWaitedSeconds,

    // How many operators are Active (potential claimants). Not the number
    // of currently connected PWAs — that would need a SignalR presence
    // scan and is deferred to PR-I.
    int ActiveOperators,

    // How many InProgress/Paused trips have an operator bound (across all
    // operators). Gives the dispatcher a "how busy is the floor" number
    // paired with PoolDepth.
    int ClaimedInFlight);
