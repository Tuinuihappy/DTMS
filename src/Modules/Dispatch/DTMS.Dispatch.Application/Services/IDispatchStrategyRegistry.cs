using DTMS.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Indexes registered <see cref="IDispatchStrategy"/> instances by
/// <see cref="TransportMode"/>. Concrete implementation auto-builds the
/// dictionary from all <c>IDispatchStrategy</c> registrations in DI —
/// so a new mode only needs <c>services.AddScoped&lt;IDispatchStrategy,
/// MyDispatchStrategy&gt;()</c> in its module extension to be picked up.
///
/// Throws <see cref="TransportModeNotEnabledException"/> when a mode is
/// requested that has no registered strategy — typically caught at the
/// command handler / order creation layer with a clear "this deployment
/// doesn't have mode X enabled" message (per ADR-006).
/// </summary>
public interface IDispatchStrategyRegistry
{
    IDispatchStrategy Get(TransportMode mode);
    bool IsRegistered(TransportMode mode);
}

/// <summary>
/// Raised when a dispatch / vendor-op is requested for a transport mode
/// that has no registered strategy in this deployment. Caller should
/// surface as 422 (mode disabled) rather than 500 (system error).
/// </summary>
public sealed class TransportModeNotEnabledException : Exception
{
    public TransportModeNotEnabledException(TransportMode mode)
        : base($"Transport mode '{mode}' is not enabled in this deployment. " +
               $"Check appsettings TransportModes:{mode}:Enabled.")
    {
        Mode = mode;
    }

    public TransportMode Mode { get; }
}
