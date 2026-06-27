using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.ForceCompleteTrip;

// Admin override for trips stuck at InProgress/Paused because the vendor's
// TASK_FINISHED webhook was dropped or never fired. Bypasses the vendor —
// no /cancel-or-complete call to RIOT3 — and just runs the same domain
// transition the webhook would have. Use when the Riot3 reconciliation
// poller can't recover the trip (e.g. RIOT3 itself never recorded the
// task as finished, so polling returns the same in-flight state).
public record ForceCompleteTripCommand(Guid TripId, string Reason, string? TriggeredBy) : ICommand;
