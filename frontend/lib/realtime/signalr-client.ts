"use client";

import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

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
  /** Resolves once the underlying connection finishes its first start(). */
  ready: Promise<void>;
};

const connections = new Map<string, ConnectionEntry>();

export function getHub(hubPath: string): HubConnection {
  const existing = connections.get(hubPath);
  if (existing) return existing.connection;

  const url = HUB_BASE_URL.replace(/\/$/, "") + hubPath;
  const connection = new HubConnectionBuilder()
    .withUrl(url, {
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      // Cookie auth flows automatically because we set withCredentials:
      // true on the underlying fetch — provided the backend CORS policy
      // allows credentials from this origin (configured in Program.cs).
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

  const ready = connection.start().catch((err) => {
    // Surface a clear error in console so the failing hook can show its
    // disconnected state. The HubConnection itself stays in the map so
    // the next subscriber's call to .invoke() short-circuits with the
    // same "Cannot send data..." error rather than racing another start.
    console.warn(`[signalr] Failed to start hub at ${url}:`, err);
  });

  connections.set(hubPath, { connection, ready });
  return connection;
}

async function cycleConnectionForDrain(
  hubPath: string,
  connection: HubConnection,
): Promise<void> {
  console.info(`[signalr] received __drain for ${hubPath} — cycling connection`);
  try {
    await connection.stop();
  } catch (err) {
    console.warn(`[signalr] stop during drain cycle failed for ${hubPath}:`, err);
  }
  try {
    await connection.start();
  } catch (err) {
    // Server might still be inside its drain settle window and rejecting
    // new connections via DrainAwareHubFilter. SignalR's automatic
    // reconnect will pick up from here — it watches for state changes.
    console.warn(`[signalr] restart during drain cycle failed for ${hubPath}:`, err);
    return;
  }
  if (typeof window !== "undefined") {
    window.dispatchEvent(
      new CustomEvent(DRAIN_CYCLE_EVENT, { detail: { hubPath } }),
    );
  }
}

/** CustomEvent name fired after a successful drain-driven reconnect.
 *  use-hub-subscription listens for this to re-invoke its Subscribe call,
 *  because server-side group memberships don't survive the stop/start cycle. */
export const DRAIN_CYCLE_EVENT = "dtms:signalr-drain-cycled";

export type DrainCycleDetail = { hubPath: string };

/** Returns the cached start() promise — useful for awaiting before invoke(). */
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
