namespace DTMS.Dispatch.Domain.Enums;

// Trip lifecycle states — unified across AMR and Manual/Fleet.
//   Created    — trip persisted, awaiting acceptance. AMR: RIOT3 TASK_PROCESSING;
//                Manual pool: an operator to Acknowledge from the pool.
//   InProgress — vendor / operator has taken the trip and is executing it.
//   Paused     — vendor placed the trip in a hold/hang state.
//   Completed / Failed / Cancelled — terminal states.
//
// The "in pool" signal for Manual/Fleet is NOT a distinct status — it is
// derived from (Status = Created ∧ DispatchedAt IS NOT NULL ∧
// ClaimedByOperatorId IS NULL). See IX_Trips_Pool.
public enum TripStatus { Created, InProgress, Paused, Completed, Failed, Cancelled }

// Why the trip is in Paused state. The vendor exposes two distinct paused
// flavours on its order state machine and each pairs with a DIFFERENT
// resume command — sending the wrong one returns E639999 "multi-level
// template fill error" because the vendor can't find the held mission
// template for an order it knows is in HANG. Captured on Trip when the
// pause webhook lands so the resume handler can pick the right command.
public enum VendorPauseSource
{
    // Operator-initiated pause (vendor event TASK_HELD). Resume with
    // CMD_ORDER_CONTINUE_FROM_HELD.
    Held = 0,

    // System-initiated hang (vendor event TASK_HANG, e.g. E230025 robot
    // mode change). Resume with CMD_ORDER_CONTINUE_FROM_HANG; the vendor
    // also self-recovers via TASK_HANG_TO_CONTINUE once the underlying
    // condition clears.
    Hang = 1
}
