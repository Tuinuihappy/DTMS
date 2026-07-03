"use client";

// WMS PR-4b (PR-D) — Operator pool REST client.
//
// Reads go directly to the network (no offline queue) — the pool list
// must be fresh, and a stale offline snapshot would be worse than
// showing an error state. Realtime SignalR updates keep the list
// current between fetches; this fetch runs on mount + on reconnect.

export type PoolTrip = {
  tripId: string;
  deliveryOrderId: string;
  orderRef: string;
  pickupCode: string;
  dropCode: string;
  itemCount: number;
  totalWeightKg: number;
  dispatchedAt: string;
  priority: number | null;
};

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(path, {
    cache: "no-store",
    credentials: "same-origin",
  });
  if (!res.ok) {
    throw new Error(`GET ${path} failed with ${res.status}`);
  }
  return (await res.json()) as T;
}

export const getPoolTrips = () => getJson<PoolTrip[]>("/api/operator/trips/pool");
