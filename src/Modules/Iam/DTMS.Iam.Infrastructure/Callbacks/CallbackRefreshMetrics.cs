using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// OpenTelemetry-compatible metrics for outbound callback-token auto-refresh.
/// Meter name <c>DTMS.Iam.CallbackRefresh</c> (registered in Program.cs).
/// <list type="bullet">
/// <item><c>callback_token_refresh_total</c> — attempts, tagged by
/// <c>system</c> and <c>result</c> (refreshed|skipped|failed|rejected|lock_busy)
/// so Grafana can alert on a rising failure rate per system.</item>
/// <item><c>dtms.callback_token.seconds_to_expiry</c> — per <c>system</c>, how
/// long the stored token has left, computed at scrape time from the last expiry
/// the refresher saw. Falling toward 0 means refreshes aren't landing — the
/// warning comes before callbacks start getting 401. Perpetual tokens report
/// nothing.</item>
/// </list>
/// Only the process that runs the refresh loop (the outbox worker) has values
/// for the gauge.
/// </summary>
public sealed class CallbackRefreshMetrics : IDisposable
{
    public const string MeterName = "DTMS.Iam.CallbackRefresh";

    private readonly Meter _meter;
    private readonly Counter<long> _refreshTotal;
    private readonly ConcurrentDictionary<string, DateTime> _expiries = new();

    public CallbackRefreshMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _refreshTotal = _meter.CreateCounter<long>(
            "callback_token_refresh_total",
            description: "Outbound callback token refresh attempts, tagged by system and result.");
        _meter.CreateObservableGauge(
            "dtms.callback_token.seconds_to_expiry",
            ObserveSecondsToExpiry,
            unit: "s",
            description: "Seconds until each system's stored outbound token expires.");
    }

    public void Record(string systemKey, string result)
        => _refreshTotal.Add(1,
            new KeyValuePair<string, object?>("system", systemKey),
            new KeyValuePair<string, object?>("result", result));

    /// <summary>Remember the stored token's expiry (null = perpetual or none).</summary>
    public void RecordExpiry(string systemKey, DateTime? expiresAtUtc)
    {
        if (expiresAtUtc is DateTime exp) _expiries[systemKey] = exp;
        else _expiries.TryRemove(systemKey, out _);
    }

    private IEnumerable<Measurement<double>> ObserveSecondsToExpiry()
    {
        var now = DateTime.UtcNow;
        foreach (var (system, exp) in _expiries)
            yield return new Measurement<double>(
                (exp - now).TotalSeconds, new KeyValuePair<string, object?>("system", system));
    }

    public void Dispose() => _meter.Dispose();
}
