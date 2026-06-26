"use client";

// Phase 4.5 — Lightweight IndexedDB-backed action queue for the operator
// PWA. Writes flow through the queue so:
//   - Online + healthy → action is sent immediately; queue is empty
//   - Online + transient failure (5xx, network blip) → retried on the
//     next online tick or "sync now" press
//   - Offline → action persists in IDB and the SW Background Sync handler
//     replays it once connectivity returns
//
// Dedupe: each enqueueAction takes a dedupeKey. If a row with the same
// key already exists pending in the queue (e.g. operator double-tapped
// the ack button), it's overwritten — last-write-wins instead of
// duplicate POSTs. Good enough for the action set we have (ack /
// pickup / drop / complete / override / register-push).
//
// Tag for SW Background Sync: 'dtms-operator-queue'. SW reads it in
// the 'sync' event handler — but Phase 4.5 also runs a foreground
// drain on every online event so iOS Safari (no Background Sync) still
// catches up the next time the user opens the PWA.
//
// What's intentionally NOT in here:
//   - Schema versioning (single store, single version)
//   - Conflict resolution (server commands are idempotent — replay-
//     safe by design, see Phase T1.5)
//   - Encryption-at-rest (operator trip data on a personal device is
//     considered fine to land in IDB plaintext for now)

const DB_NAME = "dtms-operator";
const DB_VERSION = 1;

// crypto.randomUUID is only exposed in secure contexts (HTTPS/localhost).
// The tablet hits http://<lan-ip>:3000 → it's missing → we fall back to a
// Math.random-based UUID v4. ID is local-only (IDB primary key); not used
// as a cryptographic identifier, so the entropy quality is fine.
function newId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}
const STORE = "queue";
const SYNC_TAG = "dtms-operator-queue";

export type QueuedAction = {
  id: string;
  dedupeKey: string;
  path: string;
  method: "POST" | "PUT" | "DELETE";
  body: unknown;
  createdAt: number;
  attempts: number;
  lastError: string | null;
};

export type EnqueueArgs = {
  path: string;
  method: "POST" | "PUT" | "DELETE";
  body: unknown;
  dedupeKey: string;
};

export type EnqueueResult = {
  delivered: boolean;        // true if the network call succeeded right now
  queuedAt: number | null;   // non-null when the action is sitting in IDB
};

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE)) {
        const store = db.createObjectStore(STORE, { keyPath: "id" });
        store.createIndex("dedupeKey", "dedupeKey", { unique: false });
        store.createIndex("createdAt", "createdAt", { unique: false });
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function withStore<T>(
  mode: IDBTransactionMode,
  fn: (store: IDBObjectStore) => Promise<T> | T,
): Promise<T> {
  const db = await openDb();
  return new Promise<T>((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const store = tx.objectStore(STORE);
    Promise.resolve(fn(store))
      .then((value) => {
        tx.oncomplete = () => resolve(value);
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error);
      })
      .catch(reject);
  });
}

function reqToPromise<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function findByDedupeKey(dedupeKey: string): Promise<QueuedAction | null> {
  return withStore("readonly", async (store) => {
    const index = store.index("dedupeKey");
    const result = await reqToPromise(index.get(dedupeKey));
    return (result as QueuedAction | undefined) ?? null;
  });
}

async function listAll(): Promise<QueuedAction[]> {
  return withStore("readonly", async (store) => {
    const result = await reqToPromise(store.getAll());
    return (result as QueuedAction[]).sort((a, b) => a.createdAt - b.createdAt);
  });
}

async function deleteById(id: string): Promise<void> {
  await withStore("readwrite", async (store) => {
    await reqToPromise(store.delete(id));
  });
}

async function upsert(action: QueuedAction): Promise<void> {
  await withStore("readwrite", async (store) => {
    await reqToPromise(store.put(action));
  });
}

async function sendOne(action: QueuedAction): Promise<{ ok: boolean; status: number; error: string | null }> {
  try {
    const res = await fetch(action.path, {
      method: action.method,
      headers: action.body === null ? {} : { "Content-Type": "application/json" },
      credentials: "include",
      body: action.body === null ? undefined : JSON.stringify(action.body),
    });
    if (res.ok || res.status === 204) {
      return { ok: true, status: res.status, error: null };
    }
    // 4xx is permanent — operator can't fix by retrying. Surface
    // the error so drain() drops the action instead of looping.
    if (res.status >= 400 && res.status < 500) {
      const body = (await res.json().catch(() => null)) as { message?: string; error?: string } | null;
      return {
        ok: false,
        status: res.status,
        error: body?.message ?? body?.error ?? `Request failed (${res.status}).`,
      };
    }
    return { ok: false, status: res.status, error: `Server error (${res.status}).` };
  } catch (err) {
    return {
      ok: false,
      status: 0,
      error: err instanceof Error ? err.message : "Network error.",
    };
  }
}

// Public — enqueue + try-send. The mutation handles in api/operator.ts
// call this. Returns immediately after the first attempt so the caller
// can show a status badge (delivered = success toast; queued = "saved
// offline" toast).
export async function enqueueAction(args: EnqueueArgs): Promise<EnqueueResult> {
  // 1. Overwrite any pending row with the same dedupe key.
  const existing = await findByDedupeKey(args.dedupeKey);
  const id = existing?.id ?? newId();
  const action: QueuedAction = {
    id,
    dedupeKey: args.dedupeKey,
    path: args.path,
    method: args.method,
    body: args.body,
    createdAt: existing?.createdAt ?? Date.now(),
    attempts: existing?.attempts ?? 0,
    lastError: null,
  };
  await upsert(action);

  // 2. Try to send immediately when online. Skip when navigator says
  // we're offline — the queue will replay on the next online event.
  if (typeof navigator !== "undefined" && navigator.onLine === false) {
    await registerBackgroundSync();
    return { delivered: false, queuedAt: action.createdAt };
  }
  const outcome = await sendOne(action);
  if (outcome.ok) {
    await deleteById(action.id);
    return { delivered: true, queuedAt: null };
  }
  // 4xx — drop and surface error to caller. The caller decides
  // whether to surface the message as a toast.
  if (outcome.status >= 400 && outcome.status < 500) {
    await deleteById(action.id);
    throw new Error(outcome.error ?? "Action rejected.");
  }
  // 5xx / network — keep queued, increment attempt counter.
  await upsert({ ...action, attempts: action.attempts + 1, lastError: outcome.error });
  await registerBackgroundSync();
  return { delivered: false, queuedAt: action.createdAt };
}

// Drain the queue from the foreground (online event handler, "Sync
// now" button). SW Background Sync handler does the same thing.
export async function drainQueue(): Promise<{ drained: number; remaining: number; lastError: string | null }> {
  if (typeof navigator !== "undefined" && navigator.onLine === false) {
    const all = await listAll();
    return { drained: 0, remaining: all.length, lastError: "Offline." };
  }
  let drained = 0;
  let lastError: string | null = null;
  const all = await listAll();
  for (const action of all) {
    const outcome = await sendOne(action);
    if (outcome.ok) {
      await deleteById(action.id);
      drained++;
      continue;
    }
    if (outcome.status >= 400 && outcome.status < 500) {
      // Permanent failure — drop so the queue doesn't keep looping.
      await deleteById(action.id);
      lastError = outcome.error;
      drained++;
      continue;
    }
    // Transient — keep + stop draining for now (preserves order).
    await upsert({ ...action, attempts: action.attempts + 1, lastError: outcome.error });
    lastError = outcome.error;
    break;
  }
  const remaining = await listAll();
  return { drained, remaining: remaining.length, lastError };
}

export async function getQueueDepth(): Promise<number> {
  const all = await listAll();
  return all.length;
}

export async function clearQueue(): Promise<void> {
  await withStore("readwrite", async (store) => {
    await reqToPromise(store.clear());
  });
}

async function registerBackgroundSync(): Promise<void> {
  // Background Sync API isn't supported on Safari/iOS. When absent we
  // rely on the foreground 'online' handler in OfflineQueueDrainer
  // instead. Wrap in try/catch — the registration just throws on
  // unsupported devices and we don't want that bubbling up.
  if (typeof navigator === "undefined") return;
  if (!("serviceWorker" in navigator)) return;
  try {
    const reg = await navigator.serviceWorker.ready;
    // TypeScript's lib.dom doesn't have SyncManager typings stable yet —
    // duck-type it.
    const sync = (reg as ServiceWorkerRegistration & {
      sync?: { register: (tag: string) => Promise<void> };
    }).sync;
    if (sync) await sync.register(SYNC_TAG);
  } catch {
    // Unsupported / blocked — fine.
  }
}
