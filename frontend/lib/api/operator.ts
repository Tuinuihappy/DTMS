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
  url: string;
  /** Signed policy fields. They must be appended to the form BEFORE the
   *  file — S3-compatible servers read the policy from the leading fields
   *  and reject a body that puts the file first. */
  fields: Record<string, string>;
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

// Uploads the photo bytes straight to MinIO — no DTMS round-trip for the
// photo itself. A signed form post rather than a PUT, because the policy
// carries a size ceiling and an exact content type that MinIO enforces
// before writing; a PUT URL could express neither.
export const uploadPodBytes = async (
  presigned: PresignResponse,
  blob: Blob,
): Promise<void> => {
  // The policy pins Content-Type, and MinIO ignores the file part's own
  // header. A blob that failed to re-encode would therefore be stored under
  // a type its bytes do not match — an image nothing can render. Refuse it
  // here rather than upload something broken.
  if (blob.type && blob.type !== "image/jpeg") {
    throw new Error("This photo format could not be processed. Try retaking it.");
  }

  const form = new FormData();
  // Order matters — every policy field first, file last.
  for (const [k, v] of Object.entries(presigned.fields)) form.append(k, v);
  form.append("file", blob);

  // Deliberately no Content-Type header on the request: the browser must set
  // the multipart boundary itself, and setting it by hand produces a body
  // MinIO cannot parse.
  const res = await fetch(presigned.url, { method: "POST", body: form });
  if (!res.ok) {
    // MinIO checks the signed conditions before storing anything, so these
    // are expected outcomes rather than faults.
    throw new Error(
      res.status === 400
        ? "Photo is too large to upload. Try retaking it."
        : res.status === 401
          ? "You've been signed out. Sign in again and retake the photo."
          : res.status === 403
            ? "Upload link expired. Take the photo again."
            : `POD upload failed (${res.status}).`,
    );
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
