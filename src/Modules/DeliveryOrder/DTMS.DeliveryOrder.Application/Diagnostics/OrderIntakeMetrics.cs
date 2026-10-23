using System.Diagnostics.Metrics;

namespace DTMS.DeliveryOrder.Application.Diagnostics;

/// <summary>
/// Orders accepted into planning vs refused at the door, meter
/// <c>DTMS.OrderIntake</c>. A refusal used to be invisible unless someone read
/// the api log — e.g. OMS retrying an order three times against a deactivated
/// station, or a route left without an order template after a template rework.
///
/// Exposed metrics (Prometheus names):
///   - dtms_order_intake_accepted_total{path, system}
///   - dtms_order_intake_rejected_total{path, system, reason, location}
///
/// <c>path</c>: source (POST /api/v1/source/delivery-orders) | submit |
/// bulk_submit | resend (Edit &amp; resend). <c>system</c>: the order's source
/// system key. <c>reason</c>: one of <see cref="Reasons"/>. <c>location</c>:
/// the offending station / WMS code, or "PICKUP → DROP" for a route — bounded
/// by the facility's size, empty where no location is at fault.
///
/// A refusal for undispatchable routes counts once per failing route (so the
/// "worst routes" table adds up); every other refusal counts once per order.
/// Body-validation 400s (FluentValidation, before a handler runs) are not
/// counted — iam.SystemRequestLog has those.
/// </summary>
public sealed class OrderIntakeMetrics : IDisposable
{
    public const string MeterName = "DTMS.OrderIntake";

    public static class Paths
    {
        public const string Source = "source";
        public const string Submit = "submit";
        public const string BulkSubmit = "bulk_submit";
        public const string Resend = "resend";
    }

    public static class Reasons
    {
        public const string RouteNotDispatchable = "route_not_dispatchable";
        public const string StationUnknown = "station_unknown";
        public const string StationInactive = "station_inactive";
        public const string StationOffline = "station_offline";
        public const string WmsLocationUnknown = "wms_location_unknown";
        public const string WmsLocationInactive = "wms_location_inactive";
        public const string ModeDisabled = "mode_disabled";
        public const string DuplicateRef = "duplicate_ref";
        public const string Invalid = "invalid";
    }

    private readonly Meter _meter;
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _rejected;

    public OrderIntakeMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _accepted = _meter.CreateCounter<long>(
            "dtms.order_intake.accepted.total",
            description: "Orders accepted into planning, by intake path and source system.");
        _rejected = _meter.CreateCounter<long>(
            "dtms.order_intake.rejected.total",
            description: "Orders refused at intake, by path, source system, reason and offending location.");
    }

    public void Accepted(string path, string? system, int count = 1)
        => _accepted.Add(count,
            new KeyValuePair<string, object?>("path", path),
            new KeyValuePair<string, object?>("system", Norm(system)));

    public void Rejected(string path, string? system, string reason, string? location = null)
        => _rejected.Add(1,
            new KeyValuePair<string, object?>("path", path),
            new KeyValuePair<string, object?>("system", Norm(system)),
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("location", location ?? string.Empty));

    /// <summary>One count per failing route, labelled "PICKUP → DROP".</summary>
    public void RoutesRejected(string path, string? system, IEnumerable<string> routes)
    {
        foreach (var route in routes)
            Rejected(path, system, Reasons.RouteNotDispatchable, route);
    }

    private static string Norm(string? system) => string.IsNullOrWhiteSpace(system) ? "unknown" : system;

    public void Dispose() => _meter.Dispose();
}
