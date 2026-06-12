// Phase P1 (b12) — Status history projection API client.
// One fetcher per aggregate (Order/Job/Trip); they all return the same
// shape so the shared <StatusTimelineSection /> component can render
// either with a single prop signature.

export type StatusHistoryEntry = {
  eventId: string;
  fromStatus: string | null;
  toStatus: string;
  occurredAt: string;
  reason: string | null;
};

export type StatusHistoryResponse<TIdKey extends string> = {
  entries: StatusHistoryEntry[];
  /** Most recent OccurredAt across the entries, or null when empty.
   * Frontend uses this to drive <DataFreshnessChip /> without a metadata
   * round-trip. */
  lastEventAt: string | null;
} & { [K in TIdKey]: string };

async function fetchJson<T>(path: string): Promise<T> {
  const res = await fetch(path, {
    headers: { Accept: "application/json" },
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

export async function getOrderStatusHistory(orderId: string) {
  return fetchJson<StatusHistoryResponse<"orderId">>(
    `/api/delivery-orders/${encodeURIComponent(orderId)}/status-history`,
  );
}

export async function getJobStatusHistory(jobId: string) {
  return fetchJson<StatusHistoryResponse<"jobId">>(
    `/api/planning/jobs/${encodeURIComponent(jobId)}/status-history`,
  );
}

export async function getTripStatusHistory(tripId: string) {
  return fetchJson<StatusHistoryResponse<"tripId">>(
    `/api/dispatch/trips/${encodeURIComponent(tripId)}/status-history`,
  );
}
