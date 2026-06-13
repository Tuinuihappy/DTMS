// Phase P5 — Reports client. Backed by bi.OrderFacts on the server; the
// shapes mirror the C# response DTOs so chart components consume them
// without translation. Two surfaces per report:
//   1. Summary fetch — JSON cells for the in-app preview.
//   2. CSV export    — direct browser download via the export URL.

export type OrdersReportCell = {
  priority: string;
  finalStatus: string;
  count: number;
  slaConfirmBreached: number;
  slaCompleteBreached: number;
  avgTimeToConfirmSec: number | null;
  avgTimeToCompleteSec: number | null;
};

export type OrdersReportResponse = {
  fromUtc: string;
  toUtc: string;
  totalOrders: number;
  cells: OrdersReportCell[];
};

export type OrdersReportFilters = {
  fromUtc?: string;
  toUtc?: string;
  priority?: string;
  finalStatus?: string;
  sourceSystem?: string;
};

function buildQs(f: OrdersReportFilters): string {
  const qs = new URLSearchParams();
  if (f.fromUtc) qs.set("fromUtc", f.fromUtc);
  if (f.toUtc) qs.set("toUtc", f.toUtc);
  if (f.priority) qs.set("priority", f.priority);
  if (f.finalStatus) qs.set("finalStatus", f.finalStatus);
  if (f.sourceSystem) qs.set("sourceSystem", f.sourceSystem);
  return qs.toString();
}

export async function getOrdersReportSummary(
  filters: OrdersReportFilters = {},
  signal?: AbortSignal,
): Promise<OrdersReportResponse> {
  const qs = buildQs(filters);
  const url = qs ? `/api/reports/orders-summary?${qs}` : "/api/reports/orders-summary";
  const res = await fetch(url, { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load orders report: ${res.status}`);
  return (await res.json()) as OrdersReportResponse;
}

/** Returns the URL the browser can navigate to in order to stream the CSV.
 *  Use this on an <a download> or window.location.href — do NOT fetch + JSON. */
export function ordersReportCsvUrl(filters: OrdersReportFilters = {}): string {
  const qs = buildQs(filters);
  return qs ? `/api/reports/orders-export?${qs}` : "/api/reports/orders-export";
}
