// Client for the carrier registry (ADR-019 P1).
//   GET    /api/v1/fleet/carriers?status=&carrierTypeCode=&q=&page=&pageSize=
//   GET    /api/v1/fleet/carriers/{code}
//   POST   /api/v1/fleet/carriers
//
// Lifecycle actions (maintenance, retire, delete …) land with the row-action UI.

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
