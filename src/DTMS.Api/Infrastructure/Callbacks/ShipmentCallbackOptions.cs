namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// Phase S.5 (B2) — feature flag for the shipment.started / shipment.arrived
/// fan-out consumers. Dark by default: while false the consumers return
/// immediately so the legacy OMS adapter stays the sole owner of those
/// callbacks. Flip on at cutover (Phase 3) together with disabling the legacy
/// adapter (<c>UpstreamOms__Enabled=false</c>).
/// </summary>
public sealed class ShipmentCallbackOptions
{
    public const string SectionName = "Callbacks";

    public bool ShipmentEventsEnabled { get; set; } = false;
}
