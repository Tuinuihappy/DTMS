namespace DTMS.Fleet.Domain.Enums;

/// <summary>
/// Lifecycle of a physical carrier (ADR-019).
///
/// <para><see cref="InUse"/> has no writer until P3 wires trip binding — it is
/// declared now so every guard can be written and tested against the complete
/// set from the start, rather than being retrofitted once assignments exist.</para>
///
/// <para>There is no "deleted" member: deleting removes the row entirely.
/// Retirement is the soft, history-preserving removal and is reversible via
/// un-retire; deletion is for carriers that should never have existed and is
/// gated on having no history at all.</para>
/// </summary>
public enum CarrierStatus
{
    Available,
    InUse,
    Maintenance,
    Retired
}
