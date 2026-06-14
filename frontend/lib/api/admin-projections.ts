// CC4 — Projection health client. Hits the admin endpoint that
// aggregates per-projector inbox stats across all 4 modules.

export type ProjectorStatus = "healthy" | "stale" | "idle";

export type ProjectorRow = {
  name: string;
  processed: number;
  lastProcessedAtUtc: string;
  lagSeconds: number;
  status: ProjectorStatus;
};

export type ModuleStatus = {
  module: string;
  schema: string;
  projectors: ProjectorRow[];
  inboxTotal: number;
};

export type ProjectionStatusResponse = {
  generatedAtUtc: string;
  summary: {
    totalProjectors: number;
    totalEventsProcessed: number;
    healthy: number;
    stale: number;
    idle: number;
  };
  modules: ModuleStatus[];
};

export async function getProjectionStatus(
  signal?: AbortSignal,
): Promise<ProjectionStatusResponse> {
  const res = await fetch("/api/admin/projections", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load projection status: ${res.status}`);
  return (await res.json()) as ProjectionStatusResponse;
}

// P0 Day 6 — replay trigger. Today this returns 501 because the backend
// stub (NotImplementedReplayService) throws; the dialog surfaces that as
// a clear "not yet wired" message. When the real service ships, this
// client signature stays valid.
export type ReplayRequest = {
  fromUtc: string;       // ISO timestamp
  toUtc?: string;        // ISO timestamp, optional (defaults to now)
  aggregateId?: string;  // uuid, optional (targeted vs full rebuild)
};

export type ReplaySummary = {
  projectorName: string;
  fromUtc: string;
  toUtc: string;
  eventsProcessed: number;
  eventsSkipped: number;
  eventsFailed: number;
  elapsed: string;
};

export type ReplayResult =
  | { ok: true; summary: ReplaySummary }
  | { ok: false; status: number; message: string };

export async function replayProjector(
  projectorName: string,
  body: ReplayRequest,
): Promise<ReplayResult> {
  const res = await fetch(
    `/api/admin/projections/${encodeURIComponent(projectorName)}/replay`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
  );

  if (res.ok) {
    return { ok: true, summary: (await res.json()) as ReplaySummary };
  }

  let message = `Replay failed (${res.status})`;
  try {
    const text = await res.text();
    if (text.length > 0) {
      try {
        const parsed = JSON.parse(text) as { message?: string };
        if (parsed?.message) message = parsed.message;
      } catch {
        message = text;
      }
    }
  } catch {
    // ignore — fall back to default message
  }
  return { ok: false, status: res.status, message };
}
