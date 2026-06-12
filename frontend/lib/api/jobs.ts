// Planning Jobs — surface the Phase b8/b9 Job aggregate to the operator UI.
// Each delivery order spawns one Job per (pickup, drop) station-pair group;
// the Job mirrors Trip lifecycle (Dispatched → Executing → Completed | Failed
// | Cancelled) so operators can see what really happened on RIOT3 without
// drilling into Trip details.

export type JobStatus =
  | "Created"
  | "Assigned"
  | "Committed"
  | "Executing"
  | "Completed"
  | "Failed"
  | "Dispatched"
  | "Cancelled";

export type JobDto = {
  id: string;
  deliveryOrderId: string;
  status: JobStatus;
  pattern: string;
  assignedVehicleId: string | null;
  estimatedDuration: number;
  estimatedDistance: number;
  requiredCapability: string | null;
  slaDeadline: string | null;
  planningTrace: string | null;
  legCount: number;
  transportMode: string | null;
  // Phase b8/b9 envelope-anchor + Trip lifecycle fields.
  groupIndex: number | null;
  pickupStationId: string | null;
  dropStationId: string | null;
  tripId: string | null;
  vendorOrderKey: string | null;
  failureReason: string | null;
  attemptNumber: number;
};

const TERMINAL_STATES: ReadonlyArray<JobStatus> = [
  "Completed",
  "Failed",
  "Cancelled",
];

export function isJobTerminal(s: JobStatus): boolean {
  return TERMINAL_STATES.includes(s);
}

export function isJobRetriable(s: JobStatus): boolean {
  // Phase b8 — operator /retry endpoint only accepts Failed.
  // Cancelled is terminal-intentional (see [[planning-phase-b9-job-trip-sync]]).
  return s === "Failed";
}

async function api<T>(input: string, init?: RequestInit): Promise<T> {
  const res = await fetch(input, {
    ...init,
    headers: { Accept: "application/json", ...(init?.headers ?? {}) },
  });
  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // body wasn't JSON
    }
    throw new Error(message);
  }
  const text = await res.text();
  if (text.length === 0) return undefined as unknown as T;
  return JSON.parse(text) as T;
}

export async function getJobsByOrder(orderId: string): Promise<JobDto[]> {
  const raw = await api<JobDto[]>(
    `/api/planning/jobs?orderId=${encodeURIComponent(orderId)}`,
  );
  return raw ?? [];
}

export type JobsQueueResult = {
  items: JobDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

// Phase b10-frontend.2 — paginated cross-order queue for the operator
// /delivery-orders/jobs page. `statuses` becomes a repeating `statuses=`
// query param so the backend can parse an arbitrary status set.
export async function getJobsQueue(opts: {
  statuses?: JobStatus[];
  page: number;
  pageSize: number;
}): Promise<JobsQueueResult> {
  const qs = new URLSearchParams();
  qs.set("page", String(opts.page));
  qs.set("pageSize", String(opts.pageSize));
  for (const s of opts.statuses ?? []) qs.append("statuses", s);
  return await api<JobsQueueResult>(`/api/planning/jobs/queue?${qs.toString()}`);
}

export async function getJobById(id: string): Promise<JobDto> {
  return await api<JobDto>(`/api/planning/jobs/${encodeURIComponent(id)}`);
}

// Phase b8 retry — POST returns updated JobDto envelope on either outcome
// (success → re-dispatched, or new Failed reason). Caller inspects the
// returned `status` + `failureReason` to know what happened.
export async function retryJob(id: string): Promise<JobDto> {
  return await api<JobDto>(`/api/planning/jobs/${encodeURIComponent(id)}/retry`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
}
