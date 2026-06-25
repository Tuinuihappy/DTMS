"use client";

import {
  getVapidPublicKey,
  registerPushSubscription,
  sendTestPush,
} from "@/lib/api/operator";

// Phase 4.5 — Browser push helpers. Bridges the W3C PushManager API
// (binary keys, ArrayBuffers) to DTMS's flat JSON shape that
// /api/operator/devices/register-push expects.

function urlBase64ToUint8Array(base64String: string): ArrayBuffer {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = atob(base64);
  // Allocate a fresh ArrayBuffer so the result is unambiguously
  // `ArrayBuffer` (not `ArrayBufferLike`), which PushManager.subscribe's
  // applicationServerKey expects under TS 5.7's tightened typings.
  const buf = new ArrayBuffer(raw.length);
  const view = new Uint8Array(buf);
  for (let i = 0; i < raw.length; i++) view[i] = raw.charCodeAt(i);
  return buf;
}

function arrayBufferToBase64(buf: ArrayBuffer | null): string | null {
  if (!buf) return null;
  const bytes = new Uint8Array(buf);
  let binary = "";
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

export type PushSupportStatus =
  | { kind: "supported" }
  | { kind: "unsupported"; reason: string };

export function checkPushSupport(): PushSupportStatus {
  if (typeof window === "undefined") return { kind: "unsupported", reason: "Not in a browser." };
  if (!("serviceWorker" in navigator))
    return { kind: "unsupported", reason: "Service workers unavailable." };
  if (!("PushManager" in window))
    return { kind: "unsupported", reason: "Push notifications unavailable on this device." };
  if (!("Notification" in window))
    return { kind: "unsupported", reason: "Notifications API unavailable." };
  return { kind: "supported" };
}

export async function getCurrentSubscription(): Promise<PushSubscription | null> {
  if (checkPushSupport().kind !== "supported") return null;
  const reg = await navigator.serviceWorker.ready;
  return reg.pushManager.getSubscription();
}

export async function subscribeToPush(deviceLabel: string | null): Promise<{
  ok: true;
  endpoint: string;
} | { ok: false; reason: string }> {
  const support = checkPushSupport();
  if (support.kind !== "supported") return { ok: false, reason: support.reason };

  // Request permission. Already-granted is a no-op; "denied" requires
  // the user to re-enable from system settings — no API to override.
  const perm = await Notification.requestPermission();
  if (perm !== "granted") return { ok: false, reason: "Notification permission was not granted." };

  let publicKey: string;
  try {
    const fetched = await getVapidPublicKey();
    publicKey = fetched.publicKey;
    if (!publicKey)
      return {
        ok: false,
        reason:
          "Server hasn't been issued a VAPID keypair yet. Ask an admin to run " +
          "`dotnet run -- --generate-vapid-keys` and restart the API.",
      };
  } catch (err) {
    return {
      ok: false,
      reason: err instanceof Error ? err.message : "Could not fetch VAPID key.",
    };
  }

  const reg = await navigator.serviceWorker.ready;
  const existing = await reg.pushManager.getSubscription();
  let sub: PushSubscription;
  if (existing) {
    sub = existing;
  } else {
    sub = await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    });
  }

  await registerPushSubscription({
    platform: "WebPush",
    endpoint: sub.endpoint,
    publicKey: arrayBufferToBase64(sub.getKey("p256dh")),
    authSecret: arrayBufferToBase64(sub.getKey("auth")),
    deviceLabel,
  });

  return { ok: true, endpoint: sub.endpoint };
}

export async function unsubscribeFromPush(): Promise<void> {
  const sub = await getCurrentSubscription();
  if (!sub) return;
  await sub.unsubscribe();
  // The backend rows will be evicted by Phase 4.3's gateway when push
  // attempts return 410 — no separate "remove" call needed for the
  // common case. The DELETE endpoint exists for explicit cleanup if
  // we need it from the Settings UI later.
}

export const sendTestNotification = sendTestPush;
