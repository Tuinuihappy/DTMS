using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Composition-root bridge: AMR-mode implementation of IDispatchStrategy.
// Lives in API/Adapters (not Dispatch.Application) because Phase 3 will
// have it call into Planning's IDispatchOrderTemplateService —
// Dispatch.Application must stay free of Planning references.
//
// Phase 1.2 status: REGISTERED but NOT WIRED into the production dispatch
// path. The current AMR flow is vendor-first:
//
//   DeliveryOrderValidatedConsumer
//     → DispatchOrderTemplateService.DispatchByRouteAsync()
//        → IRobotOrderDispatcher.SendAsync()        (RIOT3 POST)
//        → CreateEnvelopeTripCommand                (persist Trip with vendor key)
//
// Trip is created AFTER the vendor accepts and mints the orderKey. The
// IDispatchStrategy contract is Trip-first (Trip exists, then strategy
// runs) which doesn't fit the current flow — Phase 3 will:
//   (1) Refactor consumer to create Trip with idempotency token first
//   (2) Call IDispatchStrategy through the registry
//   (3) Strategy delegates to IRobotOrderDispatcher
//   (4) Strategy returns vendor key, caller updates Trip
//
// Until that refactor lands, this strategy throws if invoked — preventing
// accidental double-dispatch if a partial Phase 3 wires in the registry
// before the consumer refactor.
internal sealed class AmrDispatchStrategy : IDispatchStrategy
{
    private readonly IDispatchOrderTemplateService _planningDispatch;
    private readonly ILogger<AmrDispatchStrategy> _logger;

    public AmrDispatchStrategy(
        IDispatchOrderTemplateService planningDispatch,
        ILogger<AmrDispatchStrategy> logger)
    {
        // Dependencies injected so Phase 3 just needs to swap the body
        // — the wiring contract is already correct.
        _planningDispatch = planningDispatch;
        _logger = logger;
    }

    public TransportMode Mode => TransportMode.Amr;

    public Task<Result<DispatchOutcome>> DispatchAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        // Defensive: surface as a typed failure (not exception) so callers
        // that might invoke through the registry get a structured error
        // rather than an uncaught throw. Phase 3 replaces this body with
        // the delegated call to IDispatchOrderTemplateService.
        _logger.LogWarning(
            "AmrDispatchStrategy invoked for Trip {TripId} (UpperKey {UpperKey}) — " +
            "production AMR dispatch still runs through DispatchOrderTemplateService. " +
            "Phase 3 will refactor the consumer to call this strategy instead. " +
            "Returning failure to prevent accidental double-dispatch.",
            trip.Id, trip.UpperKey);

        return Task.FromResult(Result<DispatchOutcome>.Failure(
            "AmrDispatchStrategy is Phase 1.2 scaffolding. The production AMR flow " +
            "still dispatches through Planning.DispatchOrderTemplateService — calling " +
            "this strategy now would create a duplicate dispatch. Phase 3 will refactor " +
            "the flow to make this strategy the canonical entry point."));
    }
}
