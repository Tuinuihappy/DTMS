using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip;

// WMS PR-4b — Operator acknowledges + starts a Manual trip in one action.
//
// Pool dispatch is the only path today (PR-E deleted the legacy auto-assign
// strategy). A Trip lands in the shared pool at dispatch time with no
// operator bound; the first operator to invoke this command wins an atomic
// SQL CAS and claims it.
//
// Three cases the handler reconciles:
//   1. Pool claim (extension = null)
//        → run CAS. On success: reload trip → Trip.MarkVendorStarted
//          (Created → InProgress + TripStartedDomainEvent) → create
//          ManualTripExtension (with AcknowledgedAt = now) → broadcast
//          PoolTripClaimed. On failure: return AlreadyClaimed sentinel
//          so the endpoint replies 409 and the PWA toasts + refreshes.
//   2. Same operator re-tap (extension exists, extension.OperatorId = me)
//        → idempotent success. Trip and extension are already in the
//          post-claim state; nothing to mutate.
//   3. Different operator re-tap (extension exists, extension.OperatorId ≠ me)
//        → someone else already claimed this trip. Return AlreadyClaimed
//          so the caller gets the same 409 toast as the CAS-race loser.
//
// The consumer/endpoint chain guarantees the CAS is atomic (raw UPDATE
// with WHERE ClaimedByOperatorId IS NULL); we don't hold any application-
// side lock.
internal sealed class AcknowledgeTripCommandHandler : ICommandHandler<AcknowledgeTripCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;
    private readonly IOperatorRepository _operators;
    private readonly ITripItemSnapshotProvider _snapshots;
    private readonly IOperatorPoolBroadcaster _poolBroadcaster;
    private readonly IPoolMetricsSink _metrics;
    private readonly ManualDispatchOptions _options;
    private readonly ILogger<AcknowledgeTripCommandHandler> _logger;

    public AcknowledgeTripCommandHandler(
        IManualTripExtensionRepository extensions,
        ITripRepository trips,
        IOperatorRepository operators,
        ITripItemSnapshotProvider snapshots,
        IOperatorPoolBroadcaster poolBroadcaster,
        IPoolMetricsSink metrics,
        IOptions<ManualDispatchOptions> options,
        ILogger<AcknowledgeTripCommandHandler> logger)
    {
        _extensions = extensions;
        _trips = trips;
        _operators = operators;
        _snapshots = snapshots;
        _poolBroadcaster = poolBroadcaster;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result> Handle(AcknowledgeTripCommand request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // The pool claim path passes its own DispatchedAt through the
        // out-param so we can record wait time without a second DB round
        // trip on the metric-record path. Non-pool paths (idempotent
        // retap, already-claimed by someone else) skip the wait sample.
        DateTime? dispatchedAt = null;
        var result = await HandleCoreAsync(request, cancellationToken, dispatchedAtSample: v => dispatchedAt = v);
        sw.Stop();
        var outcome =
            result.IsSuccess ? PoolClaimOutcomes.Success :
            result.Error == AcknowledgeTripErrorCodes.AlreadyClaimed ? PoolClaimOutcomes.Conflict :
            PoolClaimOutcomes.Error;
        _metrics.RecordClaim(outcome, sw.Elapsed.TotalMilliseconds, dispatchedAt);
        return result;
    }

    private async Task<Result> HandleCoreAsync(
        AcknowledgeTripCommand request, CancellationToken cancellationToken,
        Action<DateTime?> dispatchedAtSample)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is not null)
            return HandleAlreadyClaimed(request, ext);
        return await HandlePoolClaimAsync(request, cancellationToken, dispatchedAtSample);
    }

    // Trip already has an extension → it was claimed at some prior point.
    // Same operator → idempotent 204; different operator → 409.
    private static Result HandleAlreadyClaimed(AcknowledgeTripCommand request, ManualTripExtension ext)
    {
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure(AcknowledgeTripErrorCodes.AlreadyClaimed);
        return Result.Success();
    }

    // No extension → this operator is trying to claim + start the trip.
    private async Task<Result> HandlePoolClaimAsync(
        AcknowledgeTripCommand request, CancellationToken cancellationToken,
        Action<DateTime?> dispatchedAtSample)
    {
        var op = await _operators.GetByIdAsync(request.OperatorId, cancellationToken);
        if (op is null)
            return Result.Failure($"Operator {request.OperatorId} not found.");

        // Fast pre-checks: identify the trip's current state so we can
        // return clearer errors than "409 CAS lost" when the caller is
        // hitting an AMR trip, a fully-completed trip, or their own
        // already-claimed trip (retry). The CAS below is still the sole
        // source of truth for the actual claim.
        var existing = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (existing is null)
            return Result.Failure($"Trip {request.TripId} not found.");
        if (existing.ClaimedByOperatorId == request.OperatorId)
            return Result.Success();
        if (existing.DispatchedAt is null)
            return Result.Failure("Trip did not come through the pool — cannot claim.");
        if (existing.Status != TripStatus.Created)
            return Result.Failure(AcknowledgeTripErrorCodes.AlreadyClaimed);

        var won = await _trips.TryClaimFromPoolAsync(
            request.TripId, request.OperatorId, cancellationToken);
        if (!won)
        {
            _logger.LogInformation(
                "[AckPool] Trip {TripId} claim lost by operator {OperatorId} — another operator got there first.",
                request.TripId, request.OperatorId);
            return Result.Failure(AcknowledgeTripErrorCodes.AlreadyClaimed);
        }

        // Reload — the earlier GetByIdAsync returned a tracked instance
        // that has stale (pre-CAS) ClaimedByOperatorId/ClaimedAt. Reload
        // from DB so the aggregate reflects the SQL update before we save.
        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Trip {request.TripId} vanished immediately after CAS — impossible unless a manual DB deletion raced us.");
        // Publish the sample now — the caller uses it to record wait
        // time on the dtms.pool.wait_seconds histogram (dispatch → claim).
        dispatchedAtSample(trip.DispatchedAt);
        var items = await _snapshots.GetForTripAsync(request.TripId, cancellationToken);

        // Transitions Created → InProgress, sets StartedAt, fires
        // TripStartedDomainEvent → outbox → TripStartedIntegrationEvent.
        // All vendor-vehicle fields are null on purpose: Manual pool trips
        // notify OMS ONCE at dispatch time (via TripDispatchedIntegrationEventV1
        // with DeliveryBy=null). The TripStarted notify at claim is skipped
        // by the OMS consumer when it detects Trip.DispatchedAt is set —
        // this avoids a duplicate POST for the same shipmentId. Operator
        // identity for the trip is stored on ManualTripExtension below;
        // there is no AMR extension to feed.
        trip.MarkVendorStarted(
            vehicleId: null,
            vendorVehicleKey: null,
            vendorVehicleName: null,
            items: items);

        // Materialize the extension with SLA windows so the watchdog +
        // admin trip-detail drawer keep working. AcknowledgedAt = now so
        // any ack-SLA alarm is immediately clear.
        var now = DateTime.UtcNow;
        var pickupDeadline = now.AddMinutes(_options.PickupSlaMinutes);
        var dropDeadline = pickupDeadline.AddMinutes(_options.DropSlaMinutes);
        var ext = ManualTripExtension.AssignToOperator(
            tripId: request.TripId,
            operatorId: request.OperatorId,
            ackDeadline: null,             // pool path acks + assigns in one atomic step
            pickupDeadline: pickupDeadline,
            dropDeadline: dropDeadline);
        ext.MarkAcknowledged();
        await _extensions.AddAsync(ext, cancellationToken);

        await _trips.UpdateAsync(trip, cancellationToken);
        await _extensions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AckPool] ✓ Operator {EmployeeCode} ({OperatorId}) claimed trip {TripId} from pool " +
            "(items={ItemCount}, order {OrderId})",
            op.EmployeeCode, op.Id, request.TripId, items.Count, trip.DeliveryOrderId);

        // Realtime "someone else got this" broadcast. Fire-and-forget;
        // every connected operator (including the winner) receives it.
        // The winner's local list already removed the card optimistically
        // on their own 204 response; the others remove now.
        await _poolBroadcaster.BroadcastClaimedAsync(
            tripId: request.TripId,
            operatorId: request.OperatorId,
            operatorName: op.DisplayName,
            claimedAt: now,
            cancellationToken: cancellationToken);

        return Result.Success();
    }
}
