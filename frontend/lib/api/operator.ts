"use client";

// Phase 4.5 — Operator API client. Thin fetch wrapper that:
//   - calls go through Next.js proxy routes (cookie → bearer happens there)
//   - mutation handles use the offline queue (writes are queued + replayed
//     if the device is offline at click time)
//   - read handles bypass the queue (always hit the network — operator
//     needs fresh state)

import { enqueueAction } from "@/lib/operator-pwa/offline-queue";

export type AssignedTrip = {
  tripId: string;
  assignedAt: string;
  acknowledgedAt: string | null;
  pickedUpAt: string | null;
  droppedAt: string | null;
  pickupDeadline: string | null;
  dropDeadline: string | null;
  pickupOverrideUsed: boolean;
  dropOverrideUsed: boolean;
};

export type OperatorProfile = {
  id: string;
  employeeCode: string;
  displayName: string;
  role: "Operator" | "Supervisor" | "Admin";
  status: "Active" | "OnLeave" | "Deactivated";
  primaryWarehouseId: string | null;
  currentTripId: string | null;
  phone: string | null;
  thumbnailUrl: string | null;
  createdAt: string;
  lastSyncedAt: string;
  certifications: Array<{
    id: string;
    type: string;
    issuedAt: string;
    expiresAt: string | null;
    isActive: boolean;
  }>;
  pushSubscriptions: Array<{
    id: string;
    platform: string;
    endpoint: string;
    deviceLabel: string | null;
    subscribedAt: string;
    lastSucceededAt: string | null;
  }>;
};

export type PresignResponse = {
  uploadUrl: string;
  objectKey: string;
  expiresAt: string;
};

// ── Reads ────────────────────────────────────────────────────────────
async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url, { credentials: "include", cache: "no-store" });
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { message?: string } | null;
    throw new Error(body?.message ?? `Request to ${url} failed (${res.status}).`);
  }
  return (await res.json()) as T;
}

export const getMyProfile = () => getJson<OperatorProfile>("/api/operator/me");
export const getAssignedTrips = () =>
  getJson<AssignedTrip[]>("/api/operator/trips/assigned");
export const getVapidPublicKey = () =>
  getJson<{ publicKey: string }>("/api/operator/push/vapid-public-key");

// ── Mutations ────────────────────────────────────────────────────────
// All trip-action calls write through the offline queue first. The
// queue calls the actual network when reachable; otherwise stores the
// action in IndexedDB and the SW Background Sync handler replays it
// once connectivity returns.
//
// Why every mutation goes through the queue (instead of "online =
// direct call, offline = queue"): consistency. If a user double-taps
// the ack button while online + slow network, the queue dedupes via
// the (path, tripId) key so we don't ack twice. The queue is fast on
// the happy path (single IDB write + immediate fetch).

export const acknowledgeTrip = (tripId: string) =>
  enqueueAction({
    path: `/api/operator/trips/${encodeURIComponent(tripId)}/acknowledge`,
    method: "POST",
    body: null,
    dedupeKey: `ack:${tripId}`,
  });

export type RecordPickupBody = {
  lat: number;
  lng: number;
  podKey: string | null;
};

export const recordPickup = (tripId: string, body: RecordPickupBody) =>
  enqueueAction({
    path: `/api/operator/trips/${encodeURIComponent(tripId)}/pickup`,
    method: "POST",
    body,
    dedupeKey: `pickup:${tripId}`,
  });

export const recordDrop = (tripId: string, body: RecordPickupBody) =>
  enqueueAction({
    path: `/api/operator/trips/${encodeURIComponent(tripId)}/drop`,
    method: "POST",
    body,
    dedupeKey: `drop:${tripId}`,
  });

export const completeTrip = (tripId: string) =>
  enqueueAction({
    path: `/api/operator/trips/${encodeURIComponent(tripId)}/complete`,
    method: "POST",
    body: null,
    dedupeKey: `complete:${tripId}`,
  });

export type SubmitOverrideBody = {
  tripId: string;
  expectedWarehouseId: string;
  lat: number;
  lng: number;
  reason: string;
  photoUrl: string | null;
};

export const submitGeofenceOverride = (body: SubmitOverrideBody) =>
  enqueueAction({
    path: "/api/operator/geofence/override-request",
    method: "POST",
    body,
    dedupeKey: `override:${body.tripId}:${body.expectedWarehouseId}`,
  });

export const presignPod = async (
  tripId: string,
  kind: "pickup" | "drop",
): Promise<PresignResponse> => {
  // Presign is intentionally NOT queued — it must happen online to
  // get a valid URL. If offline we surface the failure to the caller
  // (POD capture UI shows "you must be online to upload a photo").
  const res = await fetch("/api/operator/pod/presign", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ tripId, kind, fileExtension: "jpg" }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { message?: string } | null;
    throw new Error(body?.message ?? `Presign failed (${res.status}).`);
  }
  return (await res.json()) as PresignResponse;
};

// Uploads the photo bytes to the presigned MinIO URL. Browser's PUT
// hits MinIO directly — no DTMS round-trip for the photo itself.
export const uploadPodBytes = async (uploadUrl: string, blob: Blob): Promise<void> => {
  const res = await fetch(uploadUrl, {
    method: "PUT",
    body: blob,
    headers: { "Content-Type": blob.type || "image/jpeg" },
  });
  if (!res.ok) {
    throw new Error(`POD upload failed (${res.status}).`);
  }
};

export const registerPushSubscription = (body: {
  platform: string;
  endpoint: string;
  publicKey: string | null;
  authSecret: string | null;
  deviceLabel: string | null;
}) =>
  enqueueAction({
    path: "/api/operator/devices/register-push",
    method: "POST",
    body,
    dedupeKey: `register-push:${body.endpoint}`,
  });

export const sendTestPush = async (): Promise<void> => {
  await fetch("/api/operator/push/test", {
    method: "POST",
    credentials: "include",
  });
};
