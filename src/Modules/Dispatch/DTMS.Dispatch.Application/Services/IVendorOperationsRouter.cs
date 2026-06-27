using DTMS.DeliveryOrder.Domain.Enums;

namespace DTMS.Dispatch.Application.Services;

/// <summary>
/// Resolves the right vendor-operation adapter for a given transport
/// mode. Command handlers (PauseTrip, ResumeTrip, CancelTrip) call
/// <see cref="For"/> instead of injecting <see cref="IVendorEnvelopeOperationService"/>
/// directly — that single static binding is what previously locked
/// Dispatch to RIOT3.
///
/// Phase 1: only AMR is registered, so <see cref="For"/> returns the
/// Riot3 adapter for <see cref="TransportMode.Amr"/> and throws
/// <see cref="TransportModeNotEnabledException"/> for the others.
/// Phase 4 + 5 add Manual + Fleet adapters by registering them in their
/// module extensions — router picks them up automatically because the
/// implementation walks all <c>IVendorEnvelopeOperationService</c>
/// registrations and uses the <see cref="IVendorOperationsAdapter"/>
/// marker interface to learn which mode each handles.
/// </summary>
public interface IVendorOperationsRouter
{
    /// <summary>
    /// Get the envelope-operations adapter (cancel / pause / resume)
    /// for the requested mode. Throws if no adapter is registered for
    /// that mode — caller decides whether that's a 500 (programming
    /// error) or a 422 (mode disabled for this deployment).
    /// </summary>
    IVendorEnvelopeOperationService For(TransportMode mode);

    /// <summary>
    /// Robot-level operations (pass robot through checkpoint) are AMR-only.
    /// Returns null for modes that don't have a physical robot to nudge
    /// (Manual, Fleet). Callers check before invoking — UI button is
    /// hidden if this returns null.
    /// </summary>
    IVendorRobotOperationService? ForRobot(TransportMode mode);
}

/// <summary>
/// Marker interface that lets the router learn which mode a registered
/// adapter handles. Vendor-specific adapters implement this alongside
/// <see cref="IVendorEnvelopeOperationService"/> so the router can build
/// its mode → adapter map at construction time.
///
/// Without this marker we'd need explicit registration calls per mode
/// (router.Register(Mode.Amr, riot3Adapter)) which couples composition
/// root to every mode. The marker lets each module register its adapter
/// independently — discovery is automatic.
/// </summary>
public interface IVendorOperationsAdapter
{
    TransportMode Mode { get; }
}
