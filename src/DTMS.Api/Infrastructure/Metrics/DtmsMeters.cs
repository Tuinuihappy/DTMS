namespace DTMS.Api.Infrastructure.Metrics;

/// <summary>
/// Every DTMS-owned Meter that OpenTelemetry exports (Program.cs registers this
/// list). A Meter that isn't registered still counts, silently, and nothing is
/// exported — no error anywhere. That happened twice (DTMS.Iam.CallbackRefresh,
/// DTMS.Infrastructure.Logging), so MeterRegistrationTests fails the build when
/// a class declares a <c>MeterName</c> constant that is missing here.
/// </summary>
public static class DtmsMeters
{
    public static readonly IReadOnlyList<string> All =
    [
        // Projection lag, throughput, dedup.
        DTMS.SharedKernel.Projection.ProjectionMetrics.MeterName,
        // Hub invocations / connections / rate-limit drops.
        DTMS.Api.Realtime.Observability.HubMetrics.MeterName,
        // Workflow SLO: stuck orders, consumer retries/faults, outbox, reconciler, RIOT3 frames.
        DTMS.SharedKernel.Diagnostics.WorkflowMetrics.MeterName,
        // Operator pool: depth, claims, broadcasts.
        PoolMetrics.MeterName,
        // Requests, rejections and the busiest caller's window per traffic class.
        DTMS.Api.Infrastructure.RateLimiting.RateLimitMetrics.MeterName,
        // Outbound callback-token refresh: attempts and seconds left per system.
        DTMS.Iam.Infrastructure.Callbacks.CallbackRefreshMetrics.MeterName,
        // Batched log writers (iam.SystemRequestLog): enqueued / dropped by reason.
        DTMS.Infrastructure.Logging.BatchedLogMetrics.MeterName,
    ];
}
