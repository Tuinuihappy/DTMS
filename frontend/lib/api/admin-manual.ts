"use client";

// Phase 4.6 — Dispatcher console client SDK for the Manual operator
// admin endpoints. Mirrors the operator-side client (lib/api/operator.ts)
// but keeps Admin reads + mutations separate so the bundle splits
// cleanly between the operator PWA shell and the dispatcher app.

export type OperatorBoardRow = {
  id: string;
  employeeCode: string;
  displayName: string;
  role: "Operator" | "Supervisor" | "Admin";
  status: "Active" | "OnLeave" | "Deactivated";
  primaryWarehouseId: string | null;
  currentTripId: string | null;
  lastSyncedAt: string;
};

export type ManualTripBoardRow = {
  tripId: string;
  operatorId: string;
  assignedAt: string;
  acknowledgedAt: string | null;
  pickedUpAt: string | null;
  droppedAt: string | null;
  ackDeadline: string | null;
  pickupDeadline: string | null;
  dropDeadline: string | null;
};

export type OverrideQueueRow = {
  id: string;
  operatorId: string;
  tripId: string;
  expectedWarehouseId: string;
  reportedLatitude: number;
  reportedLongitude: number;
  distanceFromGeofenceM: number;
  reason: string;
  photoUrl: string | null;
  status: "Pending" | "Approved" | "Denied" | "Expired";
  requestedAt: string;
  expiresAt: string;
};

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url, { credentials: "include", cache: "no-store" });
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { message?: string } | null;
    throw new Error(body?.message ?? `Request to ${url} failed (${res.status}).`);
  }
  return (await res.json()) as T;
}

async function postJson(url: string, body: unknown): Promise<void> {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify(body),
  });
  if (!res.ok && res.status !== 204) {
    const out = (await res.json().catch(() => null)) as { message?: string; error?: string } | null;
    throw new Error(out?.message ?? out?.error ?? `Request failed (${res.status}).`);
  }
}

export const listOperators = () =>
  getJson<OperatorBoardRow[]>("/api/admin/manual/operators");
export const listActiveManualTrips = () =>
  getJson<ManualTripBoardRow[]>("/api/admin/manual/trips");
export const listPendingOverrides = () =>
  getJson<OverrideQueueRow[]>("/api/admin/manual/geofence-overrides");

export const approveOverride = (id: string, decidedByOperatorId: string, note: string | null) =>
  postJson(`/api/admin/manual/geofence-overrides/${id}/approve`, {
    decidedByOperatorId,
    note,
  });

export const denyOverride = (id: string, decidedByOperatorId: string, reason: string) =>
  postJson(`/api/admin/manual/geofence-overrides/${id}/deny`, {
    decidedByOperatorId,
    reason,
  });

export const reassignManualTrip = (
  tripId: string,
  newOperatorId: string,
  reason: string | null,
) =>
  postJson(`/api/admin/manual/trips/${tripId}/reassign`, {
    newOperatorId,
    reason,
  });
