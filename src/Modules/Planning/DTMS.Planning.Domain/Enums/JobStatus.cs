namespace DTMS.Planning.Domain.Enums;

public enum JobStatus
{
    Created,
    Assigned,
    Committed,
    // Phase b9 — wired to TripStartedIntegrationEvent (envelope path).
    Executing,
    // Phase #1 — wired to TripPausedIntegrationEventV1 (vendor/operator
    // pause). Reversible: TripResumedIntegrationEventV1 flips back to
    // Executing. Stays Executing as a fallback if Trip pause arrives
    // for a Job in an unexpected state.
    Paused,
    // Phase b9 — wired to TripCompletedIntegrationEvent (terminal).
    Completed,
    // Failed has two sources: dispatch-time (vendor never accepted) and
    // vendor-time via Phase b9 (TripFailedIntegrationEvent). Distinguish
    // via FailureReason text, not status.
    Failed,
    // Envelope dispatch: vendor (RIOT3) accepted the order and a Trip
    // row is linked. No DTMS-side vehicle assignment — execution is the
    // vendor's concern.
    Dispatched,
    // Phase b9 — wired to TripCancelledIntegrationEvent (terminal,
    // not retriable via Job.Retry — operator chose to abandon).
    Cancelled
}
