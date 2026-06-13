using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrdersReport;

/// <summary>
/// Phase P5 — Orders by Priority/Status report. Pivots the
/// <c>bi.OrderFacts</c> table over a UTC window and groups rows by
/// (Priority, FinalStatus). One handler powers two endpoints:
///   - JSON summary for the /reports landing tile + chart.
///   - CSV stream with one row per OrderFacts row inside the window
///     (for analyst follow-up in Excel).
/// </summary>
public record GetOrdersReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    string? Priority = null,
    string? FinalStatus = null,
    string? SourceSystem = null
) : IQuery<OrdersReportResponse>;

public record OrdersReportCell(
    string Priority,
    string FinalStatus,
    int Count,
    int SlaConfirmBreached,
    int SlaCompleteBreached,
    double? AvgTimeToConfirmSec,
    double? AvgTimeToCompleteSec);

public record OrdersReportResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalOrders,
    IReadOnlyList<OrdersReportCell> Cells);
