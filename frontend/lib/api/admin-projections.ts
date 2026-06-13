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
