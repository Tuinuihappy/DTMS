// Client for the carrier registry (ADR-019 P1).
//
// Transitions are POST; DELETE is reserved for the one call that destroys a
// row, so "bring this carrier back" and "erase this carrier" can never be one
// mistyped path segment apart.

export type CarrierStatus = "Available" | "InUse" | "Maintenance" | "Retired";

export type Carrier = {
  id: string;
  carrierCode: string;
  carrierTypeCode: string;
  barcode: string | null;
  displayName: string | null;
  status: CarrierStatus;
  maintenanceReason: string | null;
  maintenanceSince: string | null;
  currentLocationCode: string | null;
  // Always render beside currentLocationCode — a location with no timestamp
  // invites people to trust stale data.
  lastSeenAt: string | null;
  commissionedAt: string | null;
  retiredAt: string | null;
  retireReason: string | null;
};

export type PagedCarriers = {
  data: Carrier[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

export type CarrierFilters = {
  status?: string;
  carrierTypeCode?: string;
  q?: string;
  page?: number;
  pageSize?: number;
};

async function readError(res: Response): Promise<string> {
  let message = `Request failed (${res.status})`;
  try {
    const body = (await res.json()) as { message?: string; detail?: string };
    if (body?.message) message = body.message;
    else if (body?.detail) message = body.detail;
  } catch {
    // not JSON
  }
  return message;
}

export async function getCarriers(
  filters: CarrierFilters = {},
  signal?: AbortSignal,
): Promise<PagedCarriers> {
  const qs = new URLSearchParams();
  if (filters.status) qs.set("status", filters.status);
  if (filters.carrierTypeCode) qs.set("carrierTypeCode", filters.carrierTypeCode);
  if (filters.q) qs.set("q", filters.q);
  if (filters.page) qs.set("page", String(filters.page));
  if (filters.pageSize) qs.set("pageSize", String(filters.pageSize));

  const res = await fetch(`/api/fleet/carriers?${qs.toString()}`, {
    cache: "no-store",
    signal,
  });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export type CreateCarrierInput = {
  carrierCode: string;
  carrierTypeCode: string;
  barcode?: string | null;
  displayName?: string | null;
  currentLocationCode?: string | null;
  commissionedAt?: string | null;
};

export async function createCarrier(input: CreateCarrierInput): Promise<string> {
  const res = await fetch("/api/fleet/carriers", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

// ── Lifecycle ─────────────────────────────────────────────────────────────

const enc = (code: string) => encodeURIComponent(code);

async function send(path: string, method: "PUT" | "POST" | "DELETE", body?: unknown): Promise<void> {
  const res = await fetch(`/api/fleet/carriers/${path}`, {
    method,
    ...(body === undefined
      ? {}
      : { headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
  });
  // A refusal from the delete guard arrives as 409 carrying the domain's own
  // reason ("has 3 maintenance record(s)…"), so surfacing the message verbatim
  // tells the user what is blocking and what to do instead.
  if (!res.ok) throw new Error(await readError(res));
}

export type UpdateCarrierInput = {
  carrierTypeCode: string;
  barcode?: string | null;
  displayName?: string | null;
  commissionedAt?: string | null;
};

export const updateCarrier = (code: string, input: UpdateCarrierInput) =>
  send(enc(code), "PUT", input);

export const moveCarrier = (code: string, currentLocationCode: string | null) =>
  send(`${enc(code)}/location`, "PUT", { currentLocationCode });

export const setCarrierMaintenance = (code: string, reason: string) =>
  send(`${enc(code)}/maintenance`, "POST", { reason });

export const returnCarrierToService = (code: string, outcome?: string | null) =>
  send(`${enc(code)}/return-to-service`, "POST", { outcome: outcome ?? null });

export const retireCarrier = (code: string, reason: string) =>
  send(`${enc(code)}/retire`, "POST", { reason });

export const unretireCarrier = (code: string) => send(`${enc(code)}/unretire`, "POST");

export const deleteCarrier = (code: string) => send(enc(code), "DELETE");

// ── Maintenance history ───────────────────────────────────────────────────

export type CarrierMaintenanceEntry = {
  id: string;
  reason: string;
  startedAt: string;
  startedBy: string;
  endedAt: string | null;
  endedBy: string | null;
  outcome: string | null;
  isOpen: boolean;
};

export async function getCarrierMaintenanceHistory(
  code: string,
  signal?: AbortSignal,
): Promise<CarrierMaintenanceEntry[]> {
  const res = await fetch(`/api/fleet/carriers/${enc(code)}/maintenance`, {
    cache: "no-store",
    signal,
  });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}
