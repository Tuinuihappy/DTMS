"use client";

import { useSyncExternalStore } from "react";
import { vendorHealthStore, type VendorHealthSummary } from "./vendor-health-store";
import type { VendorHealthSnapshot } from "@/lib/api/admin-system-status";

const EMPTY_IDS: string[] = [];
const EMPTY_SUMMARY: VendorHealthSummary = {
  total: 0,
  healthy: 0,
  degraded: 0,
  unhealthy: 0,
  unknown: 0,
};

/**
 * Subscribe to one vendor's snapshot. The component re-renders only when
 * THIS vendor's snapshot changes (status / latency / counters / etc.).
 * Other vendors transitioning has zero impact on this component.
 */
export function useVendorSnapshot(vendor: string): VendorHealthSnapshot | undefined {
  return useSyncExternalStore(
    (cb) => vendorHealthStore.subscribeVendor(vendor, cb),
    () => vendorHealthStore.getSnapshot(vendor),
    () => undefined,
  );
}

/**
 * Subscribe to the list of vendor IDs. The component re-renders only when
 * a vendor is added or removed (set membership changes) — not on every
 * status flip. Returns a stable reference between unchanged calls.
 */
export function useVendorIds(): string[] {
  return useSyncExternalStore(
    (cb) => vendorHealthStore.subscribeIds(cb),
    () => vendorHealthStore.getIds(),
    () => EMPTY_IDS,
  );
}

/**
 * Subscribe to aggregate counts (total / healthy / degraded / unhealthy /
 * unknown). Re-renders the consumer ONLY when the aggregate numbers
 * actually move — e.g., a Healthy→Healthy snapshot update from REST poll
 * does not wake summary subscribers.
 */
export function useVendorSummary(): VendorHealthSummary {
  return useSyncExternalStore(
    (cb) => vendorHealthStore.subscribeSummary(cb),
    () => vendorHealthStore.getSummary(),
    () => EMPTY_SUMMARY,
  );
}
