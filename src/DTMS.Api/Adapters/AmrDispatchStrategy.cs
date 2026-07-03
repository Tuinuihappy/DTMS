using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Application.Services;
using DTMS.Planning.Application.Services;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Adapters;

// Composition-root bridge: AMR-mode implementation of IDispatchStrategy.
// Lives in API/Adapters (not Dispatch.Application) because it calls into
// Planning's IDispatchOrderTemplateService — Dispatch.Application must
// stay free of Planning references for the module boundary to mean anything.
//
// Phase 3c — wired into the production flow. The DeliveryOrderValidatedConsumer
// resolves a strategy by mode through IDispatchStrategyRegistry instead of
// calling DispatchByRouteAsync directly; AMR routes here, Manual routes to
// ManualDispatchStrategy (stub, Phase 4 fills it in), Fleet to FleetDispatchStrategy
// (Phase 5).
//
// AMR dispatch is vendor-first: the Trip Id is the OUTCOME of this call,
// not its input. We pass through to DispatchByRouteAsync which:
//   - Resolves OrderTemplate for the (pickup, drop) pair
//   - Builds the RIOT3 envelope payload
//   - POSTs to RIOT3
//   - Persists Trip with the vendor key (idempotent on UpperKey)
internal sealed class AmrDispatchStrategy : IDispatchStrategy
{
    private readonly IDispatchOrderTemplateService _planningDispatch;
    private readonly ILogger<AmrDispatchStrategy> _logger;

    public AmrDispatchStrategy(
        IDispatchOrderTemplateService planningDispatch,
        ILogger<AmrDispatchStrategy> logger)
    {
        _planningDispatch = planningDispatch;
        _logger = logger;
    }

    public TransportMode Mode => TransportMode.Amr;

    public IReadOnlyList<DispatchGroup> GroupItems(IReadOnlyList<DispatchGroupItem> items)
        => items
            .Where(i => i.PickupStationId.HasValue && i.DropStationId.HasValue)
            .GroupBy(i => (Pickup: i.PickupStationId!.Value, Drop: i.DropStationId!.Value))
            .Select(g => new DispatchGroup(
                PickupStationId: g.Key.Pickup,
                DropStationId:   g.Key.Drop,
                Items:           g.ToList()))
            .ToList();

    public async Task<Result<DispatchGroupOutcome>> DispatchGroupAsync(
        DispatchGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _planningDispatch.DispatchByRouteAsync(
            request.DeliveryOrderId,
            request.PickupStationId,
            request.DropStationId,
            request.UpperKey,
            attemptNumber: request.AttemptNumber,
            previousAttemptId: request.PreviousAttemptId,
            priorityOverride: request.PriorityOverride,
            appointVehicleKeyOverride: request.AppointVehicleKeyOverride,
            jobId: request.JobId,
            cancellationToken: cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "[AmrDispatch] Group {GroupIndex} ({Pickup} → {Drop}) failed: {Error}",
                request.GroupIndex, request.PickupStationId, request.DropStationId, result.Error);
            return Result<DispatchGroupOutcome>.Failure(result.Error);
        }

        return Result<DispatchGroupOutcome>.Success(new DispatchGroupOutcome(
            TripId: result.Value.TripId,
            VendorOrderKey: result.Value.VendorOrderKey,
            TemplateName: result.Value.TemplateName));
    }
}
