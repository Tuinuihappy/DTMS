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
