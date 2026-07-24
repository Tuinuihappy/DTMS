"use client";

import { HubConnectionState } from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import {
  DRAIN_CYCLE_EVENT,
  type DrainCycleDetail,
  ensureStarted,
  getHub,
} from "@/lib/realtime/signalr-client";

// Backend SignalR uses MessagePack with the default ContractlessStandardResolver.
// Two mismatches with the REST JSON shape consumers expect:
//   1. Property names — MessagePack preserves C# PascalCase (OccurredAt) while
//      ASP.NET JSON converts to camelCase (occurredAt).
//   2. DateTime — MessagePack serializes as a timestamp extension which the JS
//      decoder hydrates as a Date object, while JSON gives an ISO 8601 string.
// Frontend types/sort callbacks assume the JSON shape, so we normalize both
// here once and every hub consumer gets the REST-shape behavior for free.
function normalizeKeys(value: unknown): unknown {
  if (value instanceof Date) return value.toISOString();
  if (Array.isArray(value)) return value.map(normalizeKeys);
  if (
    value !== null &&
    typeof value === "object" &&
    Object.getPrototypeOf(value) === Object.prototype
  ) {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value as Record<string, unknown>)) {
      const key = k.length > 0 ? k.charAt(0).toLowerCase() + k.slice(1) : k;
      out[key] = normalizeKeys(v);
    }
    return out;
  }
  return value;
}

// Phase P0 Day 5 — generic hook that ties the lifecycle of a SignalR
// subscription to a React component. Used by all typed hub wrappers
// (Order/Job/Trip/Dashboard/Fleet) so the reconnect-resume + cleanup
// semantics live in one place.
//
// On mount:
//   1. Start the hub connection (idempotent — shared with other components).
//   2. Register `eventHandlers` on the connection.
//   3. Invoke `subscribeMethod(...subscribeArgs)` to join the group.
//
// On unmount:
//   1. Try to Unsubscribe (best-effort; ignored if connection already lost).
//   2. Detach all event handlers we registered.
//
// On reconnect (automatic, after a network blip):
//   - Re-invoke `subscribeMethod` so the new connection joins the same
//     group again. Browsers can't carry server-side group membership
//     across reconnects — the client must re-subscribe.
//
// Returns `{ connected, error }` so the UI can render a connection
// indicator and a fallback message. Polling-based <ConnectionIndicator />
// can also read live state via `getHubState(hubPath)`.

export type HubSubscriptionOptions = {
  /** SignalR hub path, e.g. "/hubs/orders". */
  hubPath: string;
  /** Hub method name to invoke on mount / reconnect. */
  subscribeMethod: string;
  /** Method name to invoke on unmount (best-effort). */
  unsubscribeMethod: string;
  /**
   * Arguments to pass to both subscribe and unsubscribe. Must be stable
   * across renders — the hook re-subscribes when this array changes.
   */
  subscribeArgs: ReadonlyArray<unknown>;
  /** Event name → handler. Names match the typed hub client interface. */
  eventHandlers: Record<string, (...args: unknown[]) => void>;
  /** When false, the hook does nothing (useful for "open === null" cases). */
  enabled?: boolean;
};

export function useHubSubscription({
  hubPath,
  subscribeMethod,
  unsubscribeMethod,
  subscribeArgs,
  eventHandlers,
  enabled = true,
}: HubSubscriptionOptions) {
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Live view of the caller's handlers. The subscription effect below
  // deliberately does NOT depend on handler identity (callers pass inline
  // closures every render), so the wrapped handlers registered on the
  // connection must read through this ref — otherwise they'd forever call
  // the closures captured on the FIRST render. That stale-closure bug made
  // the trips list refetch with its initial filter/page/sort on every
  // SignalR hint, flapping the table against the 15s poll during busy
  // robot windows.
  const handlersRef = useRef(eventHandlers);
  useEffect(() => {
    handlersRef.current = eventHandlers;
  });

  // Stringify args for the effect's dep array — re-subscribes when any
  // arg changes value. Caller is expected to pass primitives or ids,
  // not deep objects.
  const argsKey = JSON.stringify(subscribeArgs);
  // Handler names — re-attach if the SET of events changes. Function
  // identities are not in the dep array on purpose: callers commonly
  // pass inline functions and we don't want to re-subscribe on every
  // render. Handlers ARE updated through handlersRef above.
  const handlerNamesKey = Object.keys(eventHandlers).sort().join("|");

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;

    const connection = getHub(hubPath);
    const registeredHandlers: Array<[string, (...args: unknown[]) => void]> = [];
    let cleanupDrainListener: (() => void) | null = null;

    const attach = () => {
      for (const event of Object.keys(eventHandlers)) {
        // Dispatch through handlersRef so the connection always calls the
        // latest closure the caller rendered with, not the one captured
        // when this effect first ran.
        const wrapped = (...args: unknown[]) =>
          handlersRef.current[event]?.(...(args.map(normalizeKeys) as unknown[]));
        connection.on(event, wrapped);
        registeredHandlers.push([event, wrapped]);
      }
    };

    const subscribe = async () => {
      try {
        await connection.invoke(subscribeMethod, ...subscribeArgs);
        if (!cancelled) setConnected(true);
      } catch (err) {
        if (!cancelled) setError((err as Error).message);
      }
    };

    const init = async () => {
      try {
        await ensureStarted(hubPath);
        if (cancelled) return;
        attach();
        // Re-subscribe on reconnect so the new transport joins the group.
        const onReconnected = () => {
          setConnected(true);
          setError(null);
          void subscribe();
        };
        const onReconnecting = () => setConnected(false);
        const onClose = (err?: Error) => {
          setConnected(false);
          if (err) setError(err.message);
        };
        connection.onreconnected(onReconnected);
        connection.onreconnecting(onReconnecting);
        connection.onclose(onClose);

        // G1 Phase 3 — drain cycle: signalr-client tore the connection
        // down + reopened it in response to a server "__drain" broadcast.
        // SignalR's onreconnected only fires for failure-driven recovery,
        // not for our manual stop/start, so we listen for the bespoke
        // CustomEvent and re-invoke Subscribe to rejoin the group.
        const onDrainCycled = (e: Event) => {
          const detail = (e as CustomEvent<DrainCycleDetail>).detail;
          if (detail?.hubPath !== hubPath) return;
          if (cancelled) return;
          setError(null);
          void subscribe();
        };
        if (typeof window !== "undefined") {
          window.addEventListener(DRAIN_CYCLE_EVENT, onDrainCycled);
        }
        cleanupDrainListener = () => {
          if (typeof window !== "undefined") {
            window.removeEventListener(DRAIN_CYCLE_EVENT, onDrainCycled);
          }
        };

        await subscribe();
      } catch (err) {
        if (!cancelled) setError((err as Error).message);
      }
    };

    void init();

    return () => {
      cancelled = true;
      cleanupDrainListener?.();
      // Best-effort unsubscribe — the connection may have already gone
      // away (page closing, network blip), in which case .invoke()
      // throws. We swallow because cleanup must not throw.
      if (connection.state === HubConnectionState.Connected) {
        connection.invoke(unsubscribeMethod, ...subscribeArgs).catch(() => {});
      }
      for (const [event, handler] of registeredHandlers) {
        connection.off(event, handler);
      }
      setConnected(false);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hubPath, subscribeMethod, unsubscribeMethod, argsKey, handlerNamesKey, enabled]);

  return { connected, error };
}
