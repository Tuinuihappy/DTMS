"use client";

import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";
import { type HubLinkState, startWithRetry } from "@/lib/realtime/start-with-retry";

// Phase P0 Day 5 — singleton HubConnection manager. One connection per
// hub path is shared across components that subscribe to the same hub
// (e.g. multiple Order drawers open at once → one WebSocket, not N).
//
// Design choices:
//   - Force WebSockets + skipNegotiation → saves the negotiate roundtrip
//     (-50 ms p99 first-byte). DTMS is a controlled environment; the
//     fallback transports (SSE / long-polling) exist on the server but
//     we never expect to need them in this deployment.
//   - MessagePack protocol → 30-60% smaller payloads, 3-5× faster parse
//     than JSON. Matches what the backend negotiates first.
//   - Exponential backoff with jitter on reconnect → herds reconnect
//     attempts so a server bounce doesn't return as a connection storm.
//   - Lazy start → connection is opened on first subscriber, not at
//     import time. Pages that don't use realtime stay zero-cost.

const HUB_BASE_URL =
  process.env.NEXT_PUBLIC_DTMS_HUBS_URL ?? "http://localhost:5219";

type ConnectionEntry = {
  connection: HubConnection;
  /** Resolves once the connection is first up — retrying a failed start
   *  for as long as it takes. Never rejects. */
  ready: Promise<void>;
  /** A drain cycle is stopping/starting the connection on purpose; the
   *  onclose its stop() fires must not kick off a second start. */
  cycling: boolean;
  /** The startWithRetry loop running for this connection, if any. */
  retrying: Promise<void> | null;
};

const connections = new Map<string, ConnectionEntry>();

function linkState(connection: HubConnection): HubLinkState {
  if (connection.state === HubConnectionState.Connected) return "connected";
  if (connection.state === HubConnectionState.Disconnected) return "disconnected";
  return "busy";
}

/** Start (or restart) the entry's connection, retrying until it is up.
 *  Only one loop per connection: a second caller just waits on the first. */
function retryStart(hubPath: string, entry: ConnectionEntry): Promise<void> {
  if (entry.retrying) return entry.retrying;
  entry.retrying = startWithRetry({
    start: () => entry.connection.start(),
    state: () => linkState(entry.connection),
    sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
    random: Math.random,
    onError: (err, attempt) => {
      // Once per outage, not on every retry — the loop can run for hours
      // while the API is down.
      if (attempt === 0) {
        console.warn(`[signalr] Failed to start hub ${hubPath} — retrying until it's back:`, err);
      }
    },
  }).finally(() => {
    entry.retrying = null;
  });
  return entry.retrying;
}

/** Tell use-hub-subscription the connection came back through a path
 *  SignalR's onreconnected doesn't cover, so it rejoins its group. */
function announceRestarted(hubPath: string) {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent(DRAIN_CYCLE_EVENT, { detail: { hubPath } }));
  }
}

export function getHub(hubPath: string): HubConnection {
  const existing = connections.get(hubPath);
  if (existing) return existing.connection;

  const url = HUB_BASE_URL.replace(/\/$/, "") + hubPath;
  const connection = new HubConnectionBuilder()
    .withUrl(url, {
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      // Cross-origin WebSocket handshake — the session cookie is
      // sameSite=lax (correct for CSRF hygiene) so it won't ride
      // along on the upgrade. Instead we fetch the JWT from a same-
      // origin endpoint on every (re)connect and let SignalR append
      // it as ?access_token=… — the backend's OnMessageReceived
      // handler pulls it into ctx.Token for the JwtBearer scheme.
      // Called on the first start AND on every automatic reconnect,
      // so a token refreshed elsewhere is picked up transparently.
      accessTokenFactory: async () => {
        try {
          const res = await fetch("/api/auth/hub-token", {
            cache: "no-store",
            credentials: "same-origin",
          });
          if (!res.ok) return "";
          const body = (await res.json()) as { token?: string | null };
          return body.token ?? "";
        } catch {
          // Network glitch — return empty so SignalR fails the connect
          // and its reconnect loop retries. Better than throwing here
          // (which would surface as an unhandled rejection).
          return "";
        }
      },
      withCredentials: true,
    })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => {
        // Cap at 30s; exponential 1s → 2s → 4s → … with ±500 ms jitter.
        const base = Math.min(1_000 * Math.pow(2, ctx.previousRetryCount), 30_000);
        return base + Math.random() * 1_000;
      },
    })
    .configureLogging(
      process.env.NODE_ENV === "production" ? LogLevel.Warning : LogLevel.Information,
    )
    .build();

  // G1 Phase 3 — graceful drain handler. Backend's ConnectionDrainService
  // broadcasts "__drain" to every hub when a pod is being taken down. We
  // pre-empt SignalR's automatic-reconnect-on-failure (which would only
  // fire once the server actually drops the socket at SIGTERM, ~149s
  // baseline) by tearing the connection down ourselves and reopening
  // immediately. In K8s this lands us on a fresh pod via the LB rotation;
  // in single-host dev it reconnects to the same pod once it boots back.
  //
  // SignalR's onreconnected callback does NOT fire for a manual stop/start
  // cycle (it's reserved for failure-driven recovery), so we publish a
  // CustomEvent that use-hub-subscription listens for to rejoin its group.
  connection.on("__drain", () => {
    void cycleConnectionForDrain(hubPath, connection);
  });

  const entry: ConnectionEntry = {
    connection,
    ready: Promise.resolve(),
    cycling: false,
    retrying: null,
  };
  connections.set(hubPath, entry);

  // A failed first start used to strand the connection Disconnected for
  // good (review M15). Now `ready` resolves only once it is actually up,
  // so subscribers waiting in ensureStarted attach and Subscribe then.
  entry.ready = retryStart(hubPath, entry);

  // onclose fires when the connection ends without SignalR reconnecting
  // it: a server-side close that doesn't allow reconnect, or a stop() of
  // ours. Bring it back unless a drain cycle or retry loop owns it.
  connection.onclose(() => {
    if (entry.cycling || entry.retrying) return;
    void retryStart(hubPath, entry).then(() => {
      if (connection.state === HubConnectionState.Connected) announceRestarted(hubPath);
    });
  });

  return connection;
}

async function cycleConnectionForDrain(
  hubPath: string,
  connection: HubConnection,
): Promise<void> {
  console.info(`[signalr] received __drain for ${hubPath} — cycling connection`);
  const entry = connections.get(hubPath);
  if (!entry || entry.cycling) return;
  entry.cycling = true;
  try {
    try {
      await connection.stop();
    } catch (err) {
      console.warn(`[signalr] stop during drain cycle failed for ${hubPath}:`, err);
    }
    try {
      await connection.start();
    } catch (err) {
      // Server might still be inside its drain settle window and rejecting
      // new connections via DrainAwareHubFilter. Automatic reconnect does
      // NOT cover this (it only follows a dropped live connection), so
      // retry here until the server takes us back.
      console.warn(`[signalr] restart during drain cycle failed for ${hubPath}:`, err);
      await retryStart(hubPath, entry);
    }
  } finally {
    entry.cycling = false;
  }
  if (connection.state === HubConnectionState.Connected) announceRestarted(hubPath);
}

/** CustomEvent name fired after a successful drain-driven reconnect.
 *  use-hub-subscription listens for this to re-invoke its Subscribe call,
 *  because server-side group memberships don't survive the stop/start cycle. */
export const DRAIN_CYCLE_EVENT = "dtms:signalr-drain-cycled";

export type DrainCycleDetail = { hubPath: string };

/** Resolves once the hub's connection is first up (a failed start is
 *  retried, so this can take as long as the API is down). Await it before
 *  invoke(). Never rejects. */
export function ensureStarted(hubPath: string): Promise<void> {
  const entry = connections.get(hubPath);
  if (!entry) {
    // getHub() always wires the entry, but defensive default avoids
    // throwing on a typo'd path.
    return Promise.resolve();
  }
  return entry.ready;
}

/** Useful for components that render a connection status badge. */
export function getHubState(hubPath: string): HubConnectionState {
  return connections.get(hubPath)?.connection.state ?? HubConnectionState.Disconnected;
}

/**
 * Tear down a single hub connection. Mostly used by tests and the admin
 * "Reconnect" button — components don't normally call this because
 * connections are designed to outlive page navigations.
 */
export async function disposeHub(hubPath: string): Promise<void> {
  const entry = connections.get(hubPath);
  if (!entry) return;
  connections.delete(hubPath);
  try {
    await entry.connection.stop();
  } catch {
    // Stop failures are non-fatal — connection is being discarded anyway.
  }
}
