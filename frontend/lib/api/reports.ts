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

// ─── P5.3 — SLA breach / Top failures / Lead-time / Vendor perf ────────

export type Window = { fromUtc: string; toUtc: string };

function windowQs(w: Window): string {
  const qs = new URLSearchParams();
  qs.set("fromUtc", w.fromUtc);
  qs.set("toUtc", w.toUtc);
  return qs.toString();
}

// ── SLA breach ──
export type SlaBreachRow = {
  priority: string;
  totalOrders: number;
  confirmBreached: number;
  completeBreached: number;
  confirmBreachRate: number; // 0..1
  completeBreachRate: number;
};
export type SlaBreachReportResponse = {
  fromUtc: string;
  toUtc: string;
  totalOrders: number;
  totalConfirmBreached: number;
  totalCompleteBreached: number;
  rows: SlaBreachRow[];
};
export async function getSlaBreachReport(w: Window, signal?: AbortSignal) {
  const res = await fetch(`/api/reports/sla-breach?${windowQs(w)}`, {
    signal,
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Failed: ${res.status}`);
  return (await res.json()) as SlaBreachReportResponse;
}

// ── Top failures ──
export type FailureReasonRow = {
  reason: string;
  finalStatus: string;
  count: number;
  pctOfFailures: number;
};
export type TopFailuresReportResponse = {
  fromUtc: string;
  toUtc: string;
  totalFailedOrders: number;
  rows: FailureReasonRow[];
};
export async function getTopFailuresReport(w: Window, limit = 20, signal?: AbortSignal) {
  const qs = new URLSearchParams(windowQs(w));
  qs.set("limit", String(limit));
  const res = await fetch(`/api/reports/top-failures?${qs.toString()}`, {
    signal,
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Failed: ${res.status}`);
  return (await res.json()) as TopFailuresReportResponse;
}

// ── Lead-time distribution ──
export type LeadTimeBucket = {
  label: string;
  lowerBoundSec: number;
  upperBoundSec: number | null;
  count: number;
  pct: number;
};
export type LeadTimeReportResponse = {
  fromUtc: string;
  toUtc: string;
  totalCompleted: number;
  avgSec: number | null;
  p50Sec: number | null;
  p95Sec: number | null;
  buckets: LeadTimeBucket[];
};
export async function getLeadTimeReport(w: Window, signal?: AbortSignal) {
  const res = await fetch(`/api/reports/lead-time?${windowQs(w)}`, {
    signal,
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Failed: ${res.status}`);
  return (await res.json()) as LeadTimeReportResponse;
}

// ── Vehicle performance ── (renamed from vendor-performance in #10)
export type VehiclePerformanceRow = {
  vendorVehicleKey: string;
  totalTrips: number;
  completed: number;
  failed: number;
  cancelled: number;
  successRate: number;
  avgTimeToCompleteSec: number | null;
  p95TimeToCompleteSec: number | null;
  slaBreached: number;
};
export type VehiclePerformanceResponse = {
  fromUtc: string;
  toUtc: string;
  totalTrips: number;
  rows: VehiclePerformanceRow[];
};
export async function getVehiclePerformanceReport(w: Window, signal?: AbortSignal) {
  const res = await fetch(`/api/reports/vehicle-performance?${windowQs(w)}`, {
    signal,
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Failed: ${res.status}`);
  return (await res.json()) as VehiclePerformanceResponse;
}

// ── Job failures by category ── (Phase #9 — JobFacts.FailureCategory)
export type JobFailureCategoryTotal = {
  category: string;
  count: number;
  pct: number;
};
export type JobFailureRow = {
  category: string;
  reason: string;
  count: number;
  retriedCount: number;
  pctOfTotal: number;
};
export type JobFailuresReportResponse = {
  fromUtc: string;
  toUtc: string;
  totalFailures: number;
  categoryTotals: JobFailureCategoryTotal[];
  rows: JobFailureRow[];
};
export async function getJobFailuresReport(w: Window, signal?: AbortSignal) {
  const res = await fetch(`/api/reports/job-failures?${windowQs(w)}`, {
    signal,
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Failed: ${res.status}`);
  return (await res.json()) as JobFailuresReportResponse;
}

export function tripsExportCsvUrl(
  filters: { fromUtc?: string; toUtc?: string; vendorUpperKey?: string; finalStatus?: string } = {},
): string {
  const qs = new URLSearchParams();
  if (filters.fromUtc) qs.set("fromUtc", filters.fromUtc);
  if (filters.toUtc) qs.set("toUtc", filters.toUtc);
  if (filters.vendorUpperKey) qs.set("vendorUpperKey", filters.vendorUpperKey);
  if (filters.finalStatus) qs.set("finalStatus", filters.finalStatus);
  const s = qs.toString();
  return s ? `/api/reports/trips-export?${s}` : "/api/reports/trips-export";
}
