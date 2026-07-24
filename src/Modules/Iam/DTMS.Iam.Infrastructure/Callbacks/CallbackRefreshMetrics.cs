using System.Diagnostics.Metrics;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// OpenTelemetry-compatible metrics for outbound callback-token auto-refresh.
/// Meter name <c>DTMS.Iam.CallbackRefresh</c>. One counter,
/// <c>callback_token_refresh_total</c>, tagged by <c>system</c> and
/// <c>result</c> (refreshed|skipped|failed|rejected|lock_busy) so Grafana can
/// alert on a rising failure rate per system.
/// </summary>
public sealed class CallbackRefreshMetrics : IDisposable
{
    public const string MeterName = "DTMS.Iam.CallbackRefresh";

    private readonly Meter _meter;
    private readonly Counter<long> _refreshTotal;

    public CallbackRefreshMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _refreshTotal = _meter.CreateCounter<long>(
            "callback_token_refresh_total",
            description: "Outbound callback token refresh attempts, tagged by system and result.");
    }

    public void Record(string systemKey, string result)
        => _refreshTotal.Add(1,
            new KeyValuePair<string, object?>("system", systemKey),
            new KeyValuePair<string, object?>("result", result));

    public void Dispose() => _meter.Dispose();
}
