import type { VendorHealthSnapshot } from "@/lib/api/admin-system-status";

// Vanilla external store for vendor health snapshots. The backend pushes
// transitions through DashboardHub.VendorHealthChanged one snapshot at a
// time, and the REST poll bulk-loads every 30s. Both write straight into
// this store; React components subscribe to whichever slice they care
// about via useSyncExternalStore — a status change for postgres only
// notifies subscribers of "infra:postgres" without touching anything else.
//
// Three subscription surfaces:
//   • subscribeVendor(id, cb)  — one card's slice
//   • subscribeIds(cb)         — list shape (sections re-render only when
//                                  a vendor is added/removed)
//   • subscribeSummary(cb)     — aggregate counts (fires on ANY change)

type Listener = () => void;

type Summary = {
  total: number;
  healthy: number;
  degraded: number;
  unhealthy: number;
  unknown: number;
};

const EMPTY_SUMMARY: Summary = Object.freeze({
  total: 0,
  healthy: 0,
  degraded: 0,
  unhealthy: 0,
  unknown: 0,
}) as Summary;

class VendorHealthStore {
  private snapshots = new Map<string, VendorHealthSnapshot>();
  private perVendorListeners = new Map<string, Set<Listener>>();
  private idsListeners = new Set<Listener>();
  private summaryListeners = new Set<Listener>();

  // useSyncExternalStore requires getSnapshot() to return a stable
  // reference when nothing actually changed — otherwise React loops.
  // We cache the derived views and invalidate on writes.
  private cachedIds: string[] = [];
  private cachedSummary: Summary = EMPTY_SUMMARY;

  // ── Reads ────────────────────────────────────────────────

  getSnapshot(vendor: string): VendorHealthSnapshot | undefined {
    return this.snapshots.get(vendor);
  }

  getIds(): string[] {
    return this.cachedIds;
  }

  getSummary(): Summary {
    return this.cachedSummary;
  }

  // ── Writes ───────────────────────────────────────────────

  setSnapshot(snapshot: VendorHealthSnapshot): void {
    const previous = this.snapshots.get(snapshot.vendor);

    if (previous && equalSnapshot(previous, snapshot)) {
      // No-op write — saves a render across every subscriber.
      return;
    }

    this.snapshots.set(snapshot.vendor, snapshot);
    this.perVendorListeners.get(snapshot.vendor)?.forEach((fn) => fn());

    if (!previous) {
      this.cachedIds = [...this.snapshots.keys()].sort();
      this.idsListeners.forEach((fn) => fn());
    }

    this.recomputeSummary();
  }

  bulkSet(snapshots: VendorHealthSnapshot[]): void {
    const beforeSize = this.snapshots.size;
    let anyChanged = false;

    for (const s of snapshots) {
      const prev = this.snapshots.get(s.vendor);
      if (!prev || !equalSnapshot(prev, s)) {
        this.snapshots.set(s.vendor, s);
        this.perVendorListeners.get(s.vendor)?.forEach((fn) => fn());
        anyChanged = true;
      }
    }

    if (this.snapshots.size !== beforeSize) {
      this.cachedIds = [...this.snapshots.keys()].sort();
      this.idsListeners.forEach((fn) => fn());
    }

    if (anyChanged) {
      this.recomputeSummary();
    }
  }

  // ── Subscriptions ────────────────────────────────────────

  subscribeVendor(vendor: string, cb: Listener): () => void {
    let set = this.perVendorListeners.get(vendor);
    if (!set) {
      set = new Set();
      this.perVendorListeners.set(vendor, set);
    }
    set.add(cb);
    return () => {
      const s = this.perVendorListeners.get(vendor);
      if (!s) return;
      s.delete(cb);
      if (s.size === 0) this.perVendorListeners.delete(vendor);
    };
  }

  subscribeIds(cb: Listener): () => void {
    this.idsListeners.add(cb);
    return () => {
      this.idsListeners.delete(cb);
    };
  }

  subscribeSummary(cb: Listener): () => void {
    this.summaryListeners.add(cb);
    return () => {
      this.summaryListeners.delete(cb);
    };
  }

  // ── Internal ─────────────────────────────────────────────

  private recomputeSummary(): void {
    const next: Summary = { total: 0, healthy: 0, degraded: 0, unhealthy: 0, unknown: 0 };
    for (const s of this.snapshots.values()) {
      next.total++;
      if (s.status === "Healthy") next.healthy++;
      else if (s.status === "Degraded") next.degraded++;
      else if (s.status === "Unhealthy") next.unhealthy++;
      else next.unknown++;
    }
    if (
      next.total === this.cachedSummary.total &&
      next.healthy === this.cachedSummary.healthy &&
      next.degraded === this.cachedSummary.degraded &&
      next.unhealthy === this.cachedSummary.unhealthy &&
      next.unknown === this.cachedSummary.unknown
    ) {
      // Summary numerically identical — no subscribers need waking.
      return;
    }
    this.cachedSummary = next;
    this.summaryListeners.forEach((fn) => fn());
  }
}

function equalSnapshot(a: VendorHealthSnapshot, b: VendorHealthSnapshot): boolean {
  return (
    a.status === b.status &&
    a.latencyMs === b.latencyMs &&
    a.code === b.code &&
    a.message === b.message &&
    a.failureReason === b.failureReason &&
    a.consecutiveSuccesses === b.consecutiveSuccesses &&
    a.consecutiveFailures === b.consecutiveFailures &&
    a.lastChangedAt === b.lastChangedAt &&
    a.lastCheckedAt === b.lastCheckedAt
  );
}

export type VendorHealthSummary = Summary;
export const vendorHealthStore = new VendorHealthStore();
