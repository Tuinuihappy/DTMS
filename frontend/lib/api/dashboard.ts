// Phase P3 — Dashboard read-model client. Endpoints map directly to the
// backend projection queries; the shapes mirror the C# DTOs so existing
// chart components can consume them without a translation layer.

export type OrderFunnelBucket = {
  bucketHour: string;
  confirmed: number;
  dispatched: number;
  inProgress: number;
  completed: number;
  partiallyCompleted: number;
  failed: number;
  cancelled: number;
  rejected: number;
  held: number;
  released: number;
};

export type OrderFunnelTotals = {
  confirmed: number;
  dispatched: number;
  inProgress: number;
  completed: number;
  partiallyCompleted: number;
  failed: number;
  cancelled: number;
  rejected: number;
  held: number;
  released: number;
};

export type OrderFunnelResponse = {
  fromUtc: string;
  toUtc: string;
  buckets: OrderFunnelBucket[];
  totals: OrderFunnelTotals;
  /** MAX(BucketHour) from the response — used to drive <DataFreshnessChip />.
   * null when no buckets exist in the window. */
  lastEventAt: string | null;
};

async function fetchJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(path, {
    headers: { Accept: "application/json" },
    signal,
  });
  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string; title?: string };
      message = body?.message ?? body?.title ?? message;
    } catch {
      /* not JSON */
    }
    throw new Error(message);
  }
  return (await res.json()) as T;
}

/**
 * Hour-bucketed order status counters across the given UTC window.
 * Backend caps the window at 90 days.
 */
export async function getOrderFunnel(
  opts: { fromUtc?: string; toUtc?: string } = {},
  signal?: AbortSignal,
): Promise<OrderFunnelResponse> {
  const qs = new URLSearchParams();
  if (opts.fromUtc) qs.set("fromUtc", opts.fromUtc);
  if (opts.toUtc) qs.set("toUtc", opts.toUtc);
  const suffix = qs.toString() ? `?${qs.toString()}` : "";
  return fetchJson<OrderFunnelResponse>(`/api/dashboard/order-funnel${suffix}`, signal);
}

// ── Fleet utilization (P3.2) ────────────────────────────────────────────

export type FleetUtilizationBucket = {
  bucketHour: string;
  active: number;
  busy: number;
  idle: number;
  charging: number;
  maintenance: number;
  lowBattery: number;
  offline: number;
  total: number;
};

export type FleetUtilizationResponse = {
  fromUtc: string;
  toUtc: string;
  buckets: FleetUtilizationBucket[];
  /** Most recent snapshot row regardless of the window — drives the
   *  current-state strip on /dashboard/robots. */
  latest: FleetUtilizationBucket | null;
  lastEventAt: string | null;
};

export async function getFleetUtilization(
  opts: { fromUtc?: string; toUtc?: string } = {},
  signal?: AbortSignal,
): Promise<FleetUtilizationResponse> {
  const qs = new URLSearchParams();
  if (opts.fromUtc) qs.set("fromUtc", opts.fromUtc);
  if (opts.toUtc) qs.set("toUtc", opts.toUtc);
  const suffix = qs.toString() ? `?${qs.toString()}` : "";
  return fetchJson<FleetUtilizationResponse>(`/api/dashboard/fleet-utilization${suffix}`, signal);
}
