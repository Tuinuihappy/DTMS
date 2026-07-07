using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Services;

/// <summary>
/// Dispatch path for <b>self-managed</b> orders — those whose source system
/// (OMS/WMS/ERP) executes the physical transport itself and only reports
/// lifecycle to DTMS. Supported for <see cref="DeliveryOrder.Domain.Enums.TransportMode.Manual"/>
/// only: it replaces the operator-pool execution (not AMR's vendor-driven
/// RIOT3 lifecycle). The order still declares Manual mode — used for item
/// grouping (WMS location pair) — and when <c>SelfManaged</c> is set the
/// Planning consumer routes here instead of to the Manual pool strategy.
///
/// <para>This path creates the Trip and immediately <b>auto-acknowledges</b>
/// (Created → InProgress) and <b>auto-picks-up</b> it, attributing both to the
/// order's <c>RequestedBy</c> (carried on
/// <see cref="DispatchGroupRequest.RequestedBy"/>). It does NOT dispatch to a
/// vendor (RIOT3) or the operator pool. The source system then reports drop +
/// complete via <c>/api/v1/source/trips/*</c>.</para>
/// </summary>
public interface ISelfManagedDispatchService
{
    Task<Result<DispatchGroupOutcome>> DispatchGroupAsync(
        DispatchGroupRequest request,
        CancellationToken cancellationToken = default);
}
