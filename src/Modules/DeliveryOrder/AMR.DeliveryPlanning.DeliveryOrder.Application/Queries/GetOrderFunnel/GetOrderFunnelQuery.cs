using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderFunnel;

/// <summary>
/// Phase P3 — hourly funnel buckets across an inclusive-exclusive UTC
/// window. UI sums the relevant columns to derive KpiRail tiles and
/// renders the buckets as a stacked area / sankey for the funnel chart.
/// </summary>
public record GetOrderFunnelQuery(DateTime FromUtc, DateTime ToUtc) : IQuery<OrderFunnelResponse>;

public record OrderFunnelBucketDto(
    DateTime BucketHour,
    int Confirmed,
    int Dispatched,
    int InProgress,
    int Completed,
    int PartiallyCompleted,
    int Failed,
    int Cancelled,
    int Rejected,
    int Held,
    int Released);

public record OrderFunnelResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<OrderFunnelBucketDto> Buckets,
    OrderFunnelTotals Totals,
    DateTime? LastEventAt);

public record OrderFunnelTotals(
    int Confirmed,
    int Dispatched,
    int InProgress,
    int Completed,
    int PartiallyCompleted,
    int Failed,
    int Cancelled,
    int Rejected,
    int Held,
    int Released);
