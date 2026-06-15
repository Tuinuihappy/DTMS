// Shared DTO types + browser-side fetchers for the dispatch/trips feature.
// Mirrors backend GET /api/v1/dispatch/trips/{id}/details — the rich
// operator view that bundles aggregate state, vendor snapshot fields,
// per-mission timeline, and (optionally) the raw vendor JSON blobs.

export type TripStatus =
  | "Created"
  | "InProgress"
  | "Paused"
  | "Completed"
  | "Failed"
  | "Cancelled";

export type TripMissionDto = {
  missionIndex: number;
  missionKey: string;
  missionType: string; // "MOVE" | "ACT" | other vendor types
  state: string; // "PROCESSING" | "FINISHED" | "FAILED" | "CANCELED"
  stationName: string | null;
  actionName: string | null;
  actionType: string | null;
  resultCode: string | null;
  errorMessage: string | null;
  changeStateTime: string; // ISO-8601 — vendor's wall clock
  receivedAt: string; // ISO-8601 — DTMS wall clock
};

export type TripDetailsDto = {
  id: string;
  deliveryOrderId: string;
  status: TripStatus;
  attemptNumber: number;
  previousAttemptId: string | null;
  upperKey: string;
  vendorOrderKey: string | null;
  vendorVehicleKey: string | null;
  templateNameAtDispatch: string | null;
  priorityAtDispatch: number | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  vendorExpectedCompletionAt: string | null;
  failureReason: string | null;
  pickupStationId: string | null;
  dropStationId: string | null;
  missions: TripMissionDto[];
  vendorRequestSnapshot: string | null; // only when includeRaw=true
  vendorFinalSnapshot: string | null;
};

export type TripSummaryDto = {
  id: string;
  deliveryOrderId: string;
  // Phase b8 — Planning Job that anchored this dispatch. Pre-b8 rows
  // carry the all-zero Guid string here; the UI should treat that as
  // "no Job link" and skip the chip.
  jobId: string;
  status: TripStatus;
  upperKey: string;
  vendorOrderKey: string | null;
  attemptNumber: number;
  previousAttemptId: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
};

// ── Fetchers (client-side via Next route handlers) ──────────────────────

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
      // body wasn't JSON; keep the default message
    }
    throw new Error(message);
  }
  // 204 No Content has no body; treat the same as an empty 200 (the backend
  // returns Results.Ok() for void operations like cancel/pause/resume, which
  // is 200 + empty body). Calling res.json() on an empty body throws
  // "Unexpected end of JSON input" — read raw text and only parse when set.
  if (res.status === 204) return undefined as unknown as T;
  const text = await res.text();
  if (text.length === 0) return undefined as unknown as T;
  return JSON.parse(text) as T;
}

// .NET API serializes enums with JsonNamingPolicy.SnakeCaseUpper
// (e.g. "IN_PROGRESS"). The UI types use PascalCase so normalize at
// the boundary.
function pascalFromUpperSnake(s: string): string {
  if (!s) return s;
  if (!s.includes("_") && s[0] === s[0]?.toUpperCase()) return s; // already PascalCase
  return s
    .toLowerCase()
    .split("_")
    .map((p) => (p.length ? p[0].toUpperCase() + p.slice(1) : p))
    .join("");
}

function normalizeTrip<T extends { status?: string }>(t: T): T {
  if (typeof t.status === "string") {
    return { ...t, status: pascalFromUpperSnake(t.status) };
  }
  return t;
}

function normalizeDetails(d: TripDetailsDto): TripDetailsDto {
  return {
    ...d,
    status: pascalFromUpperSnake(d.status) as TripStatus,
    missions: (d.missions ?? []).map((m) => ({
      ...m,
      // Mission state we keep raw (UPPER) because the timeline UI matches
      // on the upper form; mission type also stays raw ("MOVE"/"ACT").
    })),
  };
}

export async function getTripDetails(
  tripId: string,
  opts: { includeRaw?: boolean } = {},
): Promise<TripDetailsDto> {
  const qs = opts.includeRaw ? "?includeRaw=true" : "";
  const raw = await api<TripDetailsDto>(`/api/dispatch/trips/${tripId}/details${qs}`);
  return normalizeDetails(raw);
}

export async function getTripsByOrder(orderId: string): Promise<TripSummaryDto[]> {
  const raw = await api<TripSummaryDto[]>(`/api/dispatch/orders/${orderId}/trips`);
  return raw.map(normalizeTrip);
}

// ── Retry history (Phase 4.1) ───────────────────────────────────────────

export type TripRetryTriggerDto = {
  id: string;
  occurredAt: string;
  retrySource: string; // "Manual" | "Automatic" | "Reopen"
  retriedBy: string | null;
  retryReason: string | null;
  originalStatus: string;
};

export type TripChainEntryDto = {
  tripId: string;
  attemptNumber: number;
  status: TripStatus;
  upperKey: string;
  vendorOrderKey: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
  isCurrent: boolean;
  retryTrigger: TripRetryTriggerDto | null;
};

export type TripRetryHistoryDto = {
  tripId: string;
  totalAttempts: number;
  attempts: TripChainEntryDto[];
};

export async function getTripRetryHistory(tripId: string): Promise<TripRetryHistoryDto> {
  const raw = await api<TripRetryHistoryDto>(`/api/dispatch/trips/${tripId}/retry-history`);
  return {
    ...raw,
    attempts: (raw.attempts ?? []).map((a) => ({
      ...a,
      status: pascalFromUpperSnake(a.status) as TripStatus,
    })),
  };
}

export function cancelTrip(tripId: string, reason: string): Promise<void> {
  const qs = `?reason=${encodeURIComponent(reason)}`;
  return api<void>(`/api/dispatch/trips/${tripId}/cancel${qs}`, { method: "POST" });
}

export function pauseTrip(tripId: string): Promise<void> {
  return api<void>(`/api/dispatch/trips/${tripId}/pause`, { method: "POST" });
}

export function resumeTrip(tripId: string): Promise<void> {
  return api<void>(`/api/dispatch/trips/${tripId}/resume`, { method: "POST" });
}

export function acknowledgeRobotPass(tripId: string): Promise<void> {
  return api<void>(`/api/dispatch/trips/${tripId}/acknowledge-robot-pass`, { method: "POST" });
}

export type RetryTripRequest = {
  source?: "Manual" | "Automatic" | "Reopen";
  retriedBy?: string | null;
  reason?: string | null;
};

export type RetryTripResponse = { newTripId: string };

export function retryTrip(
  tripId: string,
  req: RetryTripRequest = {},
): Promise<RetryTripResponse> {
  return api<RetryTripResponse>(`/api/dispatch/trips/${tripId}/retry`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      source: req.source ?? "Manual",
      retriedBy: req.retriedBy ?? null,
      reason: req.reason ?? null,
    }),
  });
}

// ── Helpers ─────────────────────────────────────────────────────────────

export const TRIP_TERMINAL_STATES: ReadonlyArray<TripStatus> = [
  "Completed",
  "Failed",
  "Cancelled",
];

export const TRIP_IN_FLIGHT_STATES: ReadonlyArray<TripStatus> = [
  "Created",
  "InProgress",
  "Paused",
];

export function isTripTerminal(s: TripStatus): boolean {
  return TRIP_TERMINAL_STATES.includes(s);
}

export function isTripInFlight(s: TripStatus): boolean {
  return TRIP_IN_FLIGHT_STATES.includes(s);
}

// ── Trip items (Phase P5.3) ─────────────────────────────────────────────
// Backed by dispatch.TripItems read model. One row per (Trip, Item)
// binding with embedded order context — the drawer can render the
// table without a second round-trip per item.

export type TripItemOrderRefDto = {
  id: string;
  orderRef: string;
  status: string;
};

export type TripItemQuantityDto = {
  value: number;
  uom: string;
};

export type TripItemDto = {
  itemPk: string;
  lotNo: string;
  itemSeq: number;
  itemStatus: string;
  pickupCode: string | null;
  dropCode: string | null;
  weightKg: number | null;
  description: string | null;
  quantity: TripItemQuantityDto | null;
  order: TripItemOrderRefDto;
  boundAt: string; // ISO-8601
  lastEventAt: string;
};

export type TripItemsResponseDto = {
  tripId: string;
  itemCount: number;
  items: TripItemDto[];
};

export async function getTripItems(tripId: string): Promise<TripItemsResponseDto> {
  return api<TripItemsResponseDto>(`/api/dispatch/trips/${tripId}/items`);
}
