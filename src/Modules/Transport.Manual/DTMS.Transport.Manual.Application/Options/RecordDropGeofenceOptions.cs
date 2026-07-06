namespace DTMS.Transport.Manual.Application.Options;

/// <summary>
/// WMS PR-3 — geofence config for the drop-scan check when the trip's
/// drop is a WMS location (not a legacy warehouse row with its own
/// per-warehouse radius).
///
/// Bound to <c>Wms:Geofence</c>. Legacy warehouse trips continue to use
/// <c>Warehouse.GeofenceRadiusM</c>; this options only applies to the
/// WMS branch of RecordDropCommandHandler.
/// </summary>
public sealed class RecordDropGeofenceOptions
{
    public const string SectionName = "Wms:Geofence";

    /// <summary>Master switch for server-strict geofence enforcement on
    /// manual pickup/drop. Default <c>true</c> preserves ADR-016 behaviour.
    /// Set <c>false</c> (Wms:Geofence:Enabled) to let operators pickup/drop
    /// without GPS — the handler then skips the location lookup and distance
    /// check entirely and does not require ReportedLat/Lng. When <c>true</c>,
    /// coordinates are mandatory so an operator can't bypass the fence by
    /// simply omitting them.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Fleet-wide default radius (meters). Overridable in config;
    /// per-zone overrides would land here as a code→radius map in a future
    /// phase if ops finds 30m too strict or lax for specific zones.</summary>
    public double DefaultRadiusM { get; set; } = 30;
}
