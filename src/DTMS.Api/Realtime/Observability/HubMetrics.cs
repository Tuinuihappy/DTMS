using System.Diagnostics.Metrics;

namespace DTMS.Api.Realtime.Observability;

/// <summary>
/// OpenTelemetry-compatible metrics for SignalR hubs. Mirrored on the
/// projection side by <c>ProjectionMetrics</c> in SharedKernel so the
/// admin dashboard can show realtime health alongside projection lag.
///
/// Exposes:
///   - dtms.signalr.hub.method.invocations_total{hub, method}
///   - dtms.signalr.hub.method.duration_ms{hub, method}  (histogram)
///   - dtms.signalr.hub.connections_total{hub}            (counter, cumulative)
///   - dtms.signalr.hub.rate_limited_total{hub, method}
///
/// Meter name <c>DTMS.SignalR</c> is registered in <c>Program.cs</c> so
/// the OTel collector subscribes to it automatically.
/// </summary>
public sealed class HubMetrics : IDisposable
{
    public const string MeterName = "DTMS.SignalR";

    private readonly Meter _meter;
    private readonly Counter<long> _invocations;
    private readonly Counter<long> _connections;
    private readonly Counter<long> _rateLimited;
    private readonly Histogram<double> _methodDuration;

    public HubMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _invocations = _meter.CreateCounter<long>(
            "dtms.signalr.hub.method.invocations_total",
            unit: "calls",
            description: "Hub method invocations (excluding rate-limited rejections).");

        _connections = _meter.CreateCounter<long>(
            "dtms.signalr.hub.connections_total",
            unit: "connections",
            description: "Cumulative count of hub connect events. Pair with disconnect to derive active count.");

        _rateLimited = _meter.CreateCounter<long>(
            "dtms.signalr.hub.rate_limited_total",
            unit: "events",
            description: "Hub method invocations rejected by the per-connection token bucket.");

        _methodDuration = _meter.CreateHistogram<double>(
            "dtms.signalr.hub.method.duration_ms",
            unit: "ms",
            description: "Hub method invocation latency in milliseconds.");
    }

    public void RecordInvocation(string hubName, string methodName, double durationMs)
    {
        _invocations.Add(1, Tag("hub", hubName), Tag("method", methodName));
        _methodDuration.Record(durationMs, Tag("hub", hubName), Tag("method", methodName));
    }

    public void RecordConnected(string hubName)
        => _connections.Add(1, Tag("hub", hubName));

    public void RecordRateLimited(string hubName, string methodName)
        => _rateLimited.Add(1, Tag("hub", hubName), Tag("method", methodName));

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    public void Dispose() => _meter.Dispose();
}
