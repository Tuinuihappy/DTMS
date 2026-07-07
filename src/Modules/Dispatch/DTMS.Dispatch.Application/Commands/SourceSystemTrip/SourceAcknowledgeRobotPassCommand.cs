using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// POST /api/v1/source/trips/{tripId}/acknowledge-robot-pass — a source
/// system acting as a remote operator nudges an AMR robot waiting at a
/// checkpoint (RIOT3 PASS). Maps to <c>Trip.AcknowledgeRobotPass(...)</c>;
/// Trip.Status is unchanged (still InProgress).
///
/// <para>Unlike the four self-managed lifecycle actions (acknowledge /
/// pickup / drop / complete) this targets a DTMS-executed AMR trip, so it
/// only succeeds when the trip carries a VendorVehicleKey. <c>SourceSystemKey</c>
/// is pinned from the authenticated principal (never the body) and the trip's
/// origin is verified against the parent order via
/// <see cref="SourceTripOriginAuthorizer"/>. <c>ActionBy</c> is recorded on the
/// ExecutionEvent audit trail as WHO nudged the robot upstream.</para>
/// </summary>
public record SourceAcknowledgeRobotPassCommand(
    Guid TripId,
    string SourceSystemKey,
    string ActionBy,
    DateTime? ActedAt = null) : ICommand;
