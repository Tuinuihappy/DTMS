using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Phase 3c stub. Manual mode reaches Planning's consumer (Phase 3a opened
// the gate by removing the "skip non-AMR" early-return) and lands here,
// but the actual operator-assignment + push-notification flow is Phase 4.
//
// Returning Failure rather than throwing means:
//   - Planning consumer marks the job + items Failed (existing path)
//   - The order ends up at Failed, not Confirmed-forever
//   - Operator sees the order in the UI with a clear "Manual dispatch
//     not yet implemented" reason, which is the right user-facing signal
//     until Phase 4 ships
//
// Registered only when TransportModes:Manual:Enabled=true (default false);
// see ModuleServiceRegistration.AddTransportManual().
internal sealed class ManualDispatchStrategy : IDispatchStrategy
{
    private readonly ILogger<ManualDispatchStrategy> _logger;

    public ManualDispatchStrategy(ILogger<ManualDispatchStrategy> logger)
    {
        _logger = logger;
    }

    public TransportMode Mode => TransportMode.Manual;

    public Task<Result<DispatchGroupOutcome>> DispatchGroupAsync(
        DispatchGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[ManualDispatch] Order {OrderId} group {Group} reached the Manual strategy stub. " +
            "Phase 4 will replace this with operator assignment + push notification.",
            request.DeliveryOrderId, request.GroupIndex);

        return Task.FromResult(Result<DispatchGroupOutcome>.Failure(
            "Manual transport mode dispatch is not yet implemented (Phase 4). " +
            "The order has been validated but no operator can be assigned until Manual mode ships."));
    }
}
