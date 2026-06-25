// Phase 4.3 — Operator PWA service worker.
//
// Responsibilities today:
//   1. Receive Web Push notifications from DTMS WebPushGateway and render
//      them to the OS notification tray with the payload's Title/Body/Url.
//   2. Route notification taps back into the PWA — Url defaults to /m/trips
//      if the payload omits it.
//
// Phase 4.5 will extend this same SW with:
//   - Offline cache for /m shell routes
//   - Background Sync queue for trip actions taken while offline
//   - Asset precache + runtime caching for the operator app shell
//
// Versioning: bump CACHE_VERSION on every release so old shells get
// purged. Today there's no cache layer; the constant exists so the
// SW.update() event always fires when this file's bytes change.
const CACHE_VERSION = 'dtms-operator-v0.4.3-skeleton';

self.addEventListener('install', () => {
  // Skip the default "waiting" phase — the operator PWA is a single
  // shell, no in-flight pages to drain.
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  // Claim all clients immediately so notification routing works on
  // first install without a page reload.
  event.waitUntil(self.clients.claim());
});

// Web Push payload arrives as a string (DTMS sends JSON). Shape matches
// IPushNotificationGateway.PushNotificationPayload:
//   { title, body, url?, tag?, icon? }
self.addEventListener('push', (event) => {
  let payload = { title: 'DTMS', body: 'You have a new notification.' };
  try {
    if (event.data) payload = { ...payload, ...event.data.json() };
  } catch (_) {
    // Fall back to text() if the gateway ever sends non-JSON
    payload.body = event.data ? event.data.text() : payload.body;
  }

  const opts = {
    body: payload.body,
    icon: payload.icon || '/icons/operator-192.png',
    badge: '/icons/operator-badge.png',
    tag: payload.tag,
    data: { url: payload.url || '/m/trips' },
    requireInteraction: false,
  };
  event.waitUntil(self.registration.showNotification(payload.title, opts));
});

// Tap handler — open the URL the payload pointed at, or focus an
// existing window if the operator already has the PWA open.
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const targetUrl = event.notification.data?.url || '/m/trips';
  event.waitUntil((async () => {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const existing = all.find((c) => c.url.includes(targetUrl));
    if (existing) {
      return existing.focus();
    }
    return self.clients.openWindow(targetUrl);
  })());
});

// Phase 4.5 — Background Sync replay. The foreground enqueue path also
// registers this tag; whichever fires first drains the queue. SW
// implementation re-opens the IDB store the page wrote into and
// replays pending rows. Permanent (4xx) failures are dropped; transient
// failures keep the row so the next sync re-tries.
const QUEUE_DB = 'dtms-operator';
const QUEUE_STORE = 'queue';

function openQueueDb() {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(QUEUE_DB, 1);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function listQueue() {
  const db = await openQueueDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(QUEUE_STORE, 'readonly');
    const req = tx.objectStore(QUEUE_STORE).getAll();
    req.onsuccess = () => resolve((req.result || []).slice().sort((a, b) => a.createdAt - b.createdAt));
    req.onerror = () => reject(req.error);
  });
}

async function deleteQueueRow(id) {
  const db = await openQueueDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(QUEUE_STORE, 'readwrite');
    const req = tx.objectStore(QUEUE_STORE).delete(id);
    req.onsuccess = () => resolve();
    req.onerror = () => reject(req.error);
  });
}

async function drainQueue() {
  const items = await listQueue();
  for (const item of items) {
    try {
      const res = await fetch(item.path, {
        method: item.method,
        headers: item.body === null ? {} : { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: item.body === null ? undefined : JSON.stringify(item.body),
      });
      if (res.ok || res.status === 204) {
        await deleteQueueRow(item.id);
        continue;
      }
      // 4xx — permanent. Drop so the queue isn't stuck.
      if (res.status >= 400 && res.status < 500) {
        await deleteQueueRow(item.id);
        continue;
      }
      // 5xx — leave + bail, retry next sync event.
      break;
    } catch (_) {
      // Network blip — leave the row, retry next sync.
      break;
    }
  }
}

self.addEventListener('sync', (event) => {
  if (event.tag !== 'dtms-operator-queue') return;
  event.waitUntil(drainQueue());
});

// Push subscription rotation — push services occasionally re-issue
// endpoint URLs. The PWA listens for this and re-POSTs to
// /api/operator/devices/register-push with the fresh subscription.
self.addEventListener('pushsubscriptionchange', (event) => {
  event.waitUntil((async () => {
    try {
      const reg = await self.registration;
      const oldEndpoint = event.oldSubscription?.endpoint;
      const sub = event.newSubscription
                || await reg.pushManager.subscribe({
                     userVisibleOnly: true,
                     applicationServerKey: event.oldSubscription?.options.applicationServerKey,
                   });
      // Best-effort — the PWA shell (Phase 4.5) will own the actual
      // POST + token attachment. SW can't read the operator's auth
      // token, so we just record the change and let the shell sync
      // next time it opens.
      console.warn('[dtms-sw] subscription rotated; PWA shell should re-register', {
        old: oldEndpoint, new: sub.endpoint,
      });
    } catch (err) {
      console.error('[dtms-sw] pushsubscriptionchange failed', err);
    }
  })());
});
