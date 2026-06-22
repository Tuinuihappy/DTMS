"use client";

import { ClipboardList } from "lucide-react";
import { motion } from "motion/react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { SectionLabel } from "@/components/primitives/section-label";
import {
  abandonStuckOrder,
  confirmOrder,
  deleteOrder,
  getOrder,
  getOrderStats,
  holdOrder,
  listOrders,
  rejectOrder,
  releaseOrder,
  submitOrder,
  type DeliveryOrderDetailDto,
  type DeliveryOrderListDto,
  type OrderStats,
  type OrderStatus,
  type Priority,
  type TransportMode,
} from "@/lib/api/delivery-orders";
import { getTripsByOrder, type TripStatus } from "@/lib/api/trips";
import { useOrderListSubscription } from "@/lib/realtime/hubs/order-hub";
import { BulkActionBar } from "./bulk-bar";
import { CancelOrderDialog } from "./cancel-dialog";
import { CreateOrderDialog } from "./create-dialog";
import { OrderDetailDrawer } from "./detail-drawer";
import { FilterBar, type StatusFilter } from "./filter-bar";
import { OrdersKpiStrip } from "./kpi-strip";
import { OrdersTable, type SortColumn, type SortDir } from "./orders-table";
import { InfiniteFooter, Pagination, type PageSize } from "./pagination";
import { PodScanDialog } from "./pod-scan-dialog";
import { RedispatchDialog } from "./redispatch-dialog";
import { ReopenDialog } from "./reopen-dialog";
import {
  StateActionDialog,
  type StateActionVariant,
} from "./state-action-dialog";
import { ToastProvider, useToast } from "./toast";
import { formatDate } from "@/lib/datetime";

// Translate the UI's StatusFilter into backend query params. Virtual
// buckets ("Active"/"Completed"/"Terminal") resolve to server-side
// WHERE Status IN (...) via the statusBucket param; a concrete enum
// goes through as `status`. "All" sends neither.
function filterToServerParams(
  f: StatusFilter,
): { status?: OrderStatus; bucket?: "active" | "completed" | "terminal" } {
  if (f === "All") return {};
  if (f === "Active") return { bucket: "active" };
  if (f === "Completed") return { bucket: "completed" };
  if (f === "Terminal") return { bucket: "terminal" };
  return { status: f };
}

// Mirror of OrderStatusBuckets.Terminal on the backend — used for the
// chip count derived from the stats endpoint. If you ever add a status
// to the terminal bucket on the server, add it here too.
const TERMINAL_STATUSES: OrderStatus[] = [
  "Held",
  "Failed",
  "Amended",
  "Cancelled",
  "Rejected",
];

function exportCsv(rows: DeliveryOrderListDto[]) {
  const headers = [
    "OrderRef",
    "Status",
    "Priority",
    "Transport",
    "Items",
    "TotalWeightKg",
    "TotalQuantity",
    "RequestedBy",
    "CreatedDate",
    "SubmittedAt",
    "Notes",
  ];
  const escape = (v: unknown) => {
    const s = v == null ? "" : String(v);
    return /["\n,]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const lines = [
    headers.join(","),
    ...rows.map((o) =>
      [
        o.orderRef,
        o.orderStatus,
        o.priority,
        o.requestedTransportMode ?? "",
        o.totalItems,
        o.totalWeightKg,
        o.totalQuantity,
        o.requestedBy ?? "",
        o.createdDate,
        o.submittedAt ?? "",
        o.notes ?? "",
      ]
        .map(escape)
        .join(","),
    ),
  ];
  const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `delivery-orders-${formatDate(new Date())}.csv`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function useDebouncedValue<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

// Whitelist of statuses we accept from the URL — guards against a hand-
// crafted ?status=PUDDING crashing the chip renderer.
const STATUS_FILTER_VALUES: StatusFilter[] = [
  "All",
  "Active",
  "Completed",
  "Terminal",
  "Draft",
  "Submitted",
  "Validated",
  "Confirmed",
  "Planning",
  "Planned",
  "Dispatched",
  "InProgress",
  "PartiallyCompleted",
  "Held",
  "Failed",
  "Amended",
  "Cancelled",
  "Rejected",
];

const PRIORITY_VALUES: Array<Priority | "All"> = ["All", "Low", "Normal", "High", "Critical"];
const TRANSPORT_VALUES: Array<TransportMode | "All"> = ["All", "Amr", "Manual", "Fleet"];
const PAGE_SIZE_VALUES: PageSize[] = [10, 25, 50, 100];
const SORT_COLUMNS: SortColumn[] = [
  "createdDate",
  "orderRef",
  "priority",
  "status",
  "totalWeightKg",
];

function ExperienceInner() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  // Filter state — hydrated from the URL on first render so a deep
  // link (or refresh) lands on the same view. Each setter writes back
  // to the URL via the effect below.
  const [statusFilter, setStatusFilter] = useState<StatusFilter>(() => {
    const raw = searchParams.get("status");
    return STATUS_FILTER_VALUES.find((s) => s === raw) ?? "All";
  });
  const [priority, setPriority] = useState<Priority | "All">(() => {
    const raw = searchParams.get("priority");
    return (PRIORITY_VALUES.find((p) => p === raw) as Priority | "All") ?? "All";
  });
  const [transportMode, setTransportMode] = useState<TransportMode | "All">(() => {
    const raw = searchParams.get("transport");
    return (TRANSPORT_VALUES.find((m) => m === raw) as TransportMode | "All") ?? "All";
  });
  const [searchInput, setSearchInput] = useState(() => searchParams.get("q") ?? "");
  const search = useDebouncedValue(searchInput, 350);
  // Phase P4 — projection-backed derived filter chips. URL-persisted so
  // a teammate can share a "show only orders with failed trips" link.
  const [hasFailedTrip, setHasFailedTrip] = useState<boolean>(
    () => searchParams.get("hasFailedTrip") === "true",
  );
  const [hasActiveJob, setHasActiveJob] = useState<boolean>(
    () => searchParams.get("hasActiveJob") === "true",
  );
  // Phase P4 — pagination mode toggle (persisted in localStorage so the
  // operator keeps their preference across visits).
  const [paginationMode, setPaginationMode] = useState<"paged" | "infinite">(() => {
    if (typeof window === "undefined") return "paged";
    const stored = window.localStorage.getItem("orders:pagination-mode");
    return stored === "infinite" ? "infinite" : "paged";
  });

  // Pagination state
  const [page, setPage] = useState(() => {
    const n = Number(searchParams.get("page"));
    return Number.isFinite(n) && n > 0 ? Math.floor(n) : 1;
  });
  const [pageSize, setPageSize] = useState<PageSize>(() => {
    const n = Number(searchParams.get("size")) as PageSize;
    return PAGE_SIZE_VALUES.includes(n) ? n : 10;
  });
  // Sort state — column + direction. Defaults to newest-first which is
  // the convention every other ops dashboard in the codebase uses.
  const [sortBy, setSortBy] = useState<SortColumn>(() => {
    const raw = searchParams.get("sortBy") as SortColumn | null;
    return raw && SORT_COLUMNS.includes(raw) ? raw : "createdDate";
  });
  const [sortDir, setSortDir] = useState<SortDir>(() =>
    searchParams.get("sortDir") === "asc" ? "asc" : "desc",
  );

  // Click a column header: same column → flip direction, new column →
  // reset to descending (largest/most-recent first feels natural for
  // dates and totals; alphabetical ascends so we override below).
  const handleSortChange = useCallback(
    (col: SortColumn) => {
      if (col === sortBy) {
        setSortDir((d) => (d === "asc" ? "desc" : "asc"));
      } else {
        setSortBy(col);
        // For text columns ascending reads more naturally as a first
        // click (A→Z); for numeric/date columns descending is the ops
        // default.
        setSortDir(col === "orderRef" ? "asc" : "desc");
      }
    },
    [sortBy],
  );

  // Saved-filter snapshot: bundles every user-tunable list filter so a
  // "Save" persists the exact view, and "Apply" restores it in one shot.
  const savedFilterSnapshot = useMemo(
    () => ({
      statusFilter,
      priority,
      transportMode,
      search,
      hasFailedTrip,
      hasActiveJob,
      sortBy,
      sortDir,
    }),
    [statusFilter, priority, transportMode, search, hasFailedTrip, hasActiveJob, sortBy, sortDir],
  );

  const applySavedFilter = useCallback((snap: Record<string, unknown>) => {
    if (typeof snap.statusFilter === "string") setStatusFilter(snap.statusFilter as StatusFilter);
    if (typeof snap.priority === "string") setPriority(snap.priority as Priority | "All");
    if (typeof snap.transportMode === "string")
      setTransportMode(snap.transportMode as TransportMode | "All");
    if (typeof snap.search === "string") setSearchInput(snap.search);
    if (typeof snap.hasFailedTrip === "boolean") setHasFailedTrip(snap.hasFailedTrip);
    if (typeof snap.hasActiveJob === "boolean") setHasActiveJob(snap.hasActiveJob);
    if (typeof snap.sortBy === "string") setSortBy(snap.sortBy as SortColumn);
    if (snap.sortDir === "asc" || snap.sortDir === "desc") setSortDir(snap.sortDir);
  }, []);

  // Data state
  const [orders, setOrders] = useState<DeliveryOrderListDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [stats, setStats] = useState<OrderStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // UI state
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [detailId, setDetailId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  // Edit mode reuses the create dialog with prefill. We hold the
  // fully-fetched detail (not the list dto) so advanced fields like
  // hazmat/dimensions are available for preservation on save.
  const [editingOrder, setEditingOrder] = useState<DeliveryOrderDetailDto | null>(null);
  const [busy, setBusy] = useState(false);
  // Cancel dialog state — single-target uses `cancelTarget`; bulk uses
  // `bulkCancelTargets` (the dialog component is the same, but bulk
  // wiring fans the reason out across every selected order).
  const [cancelTarget, setCancelTarget] = useState<DeliveryOrderListDto | null>(null);
  const [bulkCancelTargets, setBulkCancelTargets] =
    useState<DeliveryOrderListDto[] | null>(null);
  // Reopen dialog state — admin override for Failed orders. Captures
  // reopenedBy + reason; the dialog calls /reopen directly so we don't
  // need to thread another action through runAction's optimistic flow.
  const [reopenTarget, setReopenTarget] = useState<DeliveryOrderListDto | null>(null);
  const [reopenBusy, setReopenBusy] = useState(false);
  const [reopenError, setReopenError] = useState<string | null>(null);
  const [redispatchTarget, setRedispatchTarget] = useState<DeliveryOrderListDto | null>(null);
  const [redispatchBusy, setRedispatchBusy] = useState(false);
  const [redispatchError, setRedispatchError] = useState<string | null>(null);
  // Hold / Release / Reject share one dialog component (StateActionDialog)
  // — the variant decides labels, tone, and which fields are required.
  const [stateActionTarget, setStateActionTarget] = useState<{
    variant: StateActionVariant;
    order: DeliveryOrderListDto;
  } | null>(null);
  const [stateActionBusy, setStateActionBusy] = useState(false);
  const [stateActionError, setStateActionError] = useState<string | null>(null);
  const [podTarget, setPodTarget] = useState<{ orderId: string; orderRef: string; itemId: string; itemLabel: string } | null>(null);
  const [podBusy, setPodBusy] = useState(false);
  const [podError, setPodError] = useState<string | null>(null);
  // In-flight trip count for the cancel cascade callout. Populated when
  // cancelTarget is set; resets to 0 when dialog closes.
  const [cancelTripCount, setCancelTripCount] = useState(0);

  const toast = useToast();
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const fetchAbortRef = useRef<AbortController | null>(null);
  // Holds the latest `runAction` closure. Bulk-cancel uses this ref
  // rather than including runAction in its deps, which would otherwise
  // be circular (runAction → openEdit → toast → … → orders).
  const runActionRef = useRef<
    | ((
        action:
          | "edit"
          | "submit"
          | "confirm"
          | "delete"
          | "reopen"
          | "redispatch"
          | "hold"
          | "release"
          | "reject"
          | "abandon",
        id: string,
        reason?: string,
      ) => Promise<void>)
    | null
  >(null);
  // Delayed-write tracking for cancel-with-undo. Each pending cancel
  // holds a timer (fires the real DELETE) plus the order snapshot used
  // to restore the row on undo. Keyed by order id so multiple in-flight
  // cancellations don't trample each other.
  const pendingCancelsRef = useRef<
    Map<
      string,
      {
        timer: ReturnType<typeof setTimeout>;
        snapshot: DeliveryOrderListDto;
        reason: string;
      }
    >
  >(new Map());

  // Reset to page 1 whenever a filter changes — otherwise the user might
  // be stranded on page 7 of a 12-row result.
  useEffect(() => {
    setPage(1);
  }, [statusFilter, priority, transportMode, search, hasFailedTrip, hasActiveJob, pageSize]);

  // Mirror state back to the URL so refresh/back/forward and share links
  // restore the view. router.replace (not push) keeps history clean.
  // Defaults are omitted from the URL so a "clean" view stays clean.
  useEffect(() => {
    const p = new URLSearchParams();
    if (statusFilter !== "All") p.set("status", statusFilter);
    if (priority !== "All") p.set("priority", priority);
    if (transportMode !== "All") p.set("transport", transportMode);
    if (search) p.set("q", search);
    if (hasFailedTrip) p.set("hasFailedTrip", "true");
    if (hasActiveJob) p.set("hasActiveJob", "true");
    if (page > 1) p.set("page", String(page));
    if (pageSize !== 10) p.set("size", String(pageSize));
    if (sortBy !== "createdDate") p.set("sortBy", sortBy);
    if (sortDir !== "desc") p.set("sortDir", sortDir);
    const qs = p.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }, [statusFilter, priority, transportMode, search, hasFailedTrip, hasActiveJob, page, pageSize, sortBy, sortDir, router, pathname]);

  // Persist pagination mode preference across visits.
  useEffect(() => {
    if (typeof window === "undefined") return;
    window.localStorage.setItem("orders:pagination-mode", paginationMode);
  }, [paginationMode]);

  const fetchOrders = useCallback(
    async (opts?: { silent?: boolean }) => {
      // Cancel any in-flight request so a slow response can't overwrite
      // a newer one (race when the user types fast or pages quickly).
      fetchAbortRef.current?.abort();
      const controller = new AbortController();
      fetchAbortRef.current = controller;

      if (!opts?.silent) setRefreshing(true);
      setError(null);
      try {
        const { status, bucket } = filterToServerParams(statusFilter);
        const res = await listOrders(
          {
            status,
            bucket,
            priority: priority === "All" ? undefined : priority,
            transportMode: transportMode === "All" ? undefined : transportMode,
            search: search.trim() || undefined,
            hasFailedTrip: hasFailedTrip || undefined,
            hasActiveJob: hasActiveJob || undefined,
            page,
            pageSize,
            sortBy,
            sortDir,
          },
          controller.signal,
        );
        // Server now handles Active/Completed buckets via WHERE Status IN
        // (...). totalCount is authoritative for pagination — no more
        // client-side post-filter or upper-bound counts.
        // In infinite-scroll mode, page>1 appends rather than replaces so
        // a "Load more" tap extends the list instead of swapping it.
        if (paginationMode === "infinite" && page > 1) {
          setOrders((prev) => {
            const seen = new Set(prev.map((o) => o.id));
            return [...prev, ...res.items.filter((o) => !seen.has(o.id))];
          });
        } else {
          setOrders(res.items);
        }
        setTotalCount(res.totalCount);
      } catch (e) {
        if ((e as Error).name === "AbortError") return;
        setError((e as Error).message || "Failed to load orders.");
      } finally {
        if (fetchAbortRef.current === controller) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [statusFilter, priority, transportMode, search, hasFailedTrip, hasActiveJob, page, pageSize, sortBy, sortDir, paginationMode],
  );

  // Stats refetch — runs less often than the table fetch and ignores
  // the search/priority filters so the KPI strip stays a stable system
  // overview, not a "narrowed view" reading.
  const fetchStats = useCallback(async () => {
    try {
      const next = await getOrderStats();
      setStats(next);
    } catch {
      // Stats failure shouldn't crash the page; the chips just lose their counts.
    }
  }, []);

  useEffect(() => {
    fetchOrders();
  }, [fetchOrders]);

  useEffect(() => {
    fetchStats();
  }, [fetchStats]);

  // Soft polling — refresh data + stats every 15s while the user isn't
  // in a modal/drawer AND the tab is visible. Backgrounded tabs would
  // otherwise keep hitting the API silently; visibilitychange lets us
  // pause and resume cleanly. When the user comes back we fire one
  // immediate fetch so they don't stare at stale numbers waiting for
  // the next interval tick.
  useEffect(() => {
    if (createOpen || detailId) return;

    const start = () => {
      if (pollRef.current) return;
      pollRef.current = setInterval(() => {
        fetchOrders({ silent: true });
        fetchStats();
      }, 15_000);
    };
    const stop = () => {
      if (pollRef.current) {
        clearInterval(pollRef.current);
        pollRef.current = null;
      }
    };

    const handleVisibility = () => {
      if (document.hidden) {
        stop();
      } else {
        start();
        fetchOrders({ silent: true });
        fetchStats();
      }
    };

    if (!document.hidden) start();
    document.addEventListener("visibilitychange", handleVisibility);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibility);
      stop();
    };
  }, [createOpen, detailId, fetchOrders, fetchStats]);

  // Phase P4 — SignalR live updates for the cross-order list. Backend
  // pushes ListItemUpdated hints to the "orders-list" group whenever any
  // row in the OrderListView projection changes. We debounce-refetch
  // (500ms) so a burst of events (e.g. many JobCreated in quick
  // succession) collapses into one round-trip. Refetch — not delta
  // merge — keeps server-side search/facets authoritative.
  const listHintTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scheduleListRefetch = useCallback(() => {
    if (listHintTimerRef.current) clearTimeout(listHintTimerRef.current);
    listHintTimerRef.current = setTimeout(() => {
      listHintTimerRef.current = null;
      fetchOrders({ silent: true });
      fetchStats();
    }, 500);
  }, [fetchOrders, fetchStats]);

  useEffect(() => {
    return () => {
      if (listHintTimerRef.current) clearTimeout(listHintTimerRef.current);
    };
  }, []);

  useOrderListSubscription({
    ListItemUpdated: scheduleListRefetch,
  });

  // Counts for the chip row come from the unfiltered stats endpoint, with
  // synthetic "All", "Active", "Completed" derived from stats.byStatus.
  const counts = useMemo<Partial<Record<StatusFilter, number>>>(() => {
    if (!stats) return {};
    const c: Partial<Record<StatusFilter, number>> = {
      All: stats.total,
      Active: stats.active,
      Completed: stats.completed,
      Terminal: TERMINAL_STATUSES.reduce(
        (sum, s) => sum + (stats.byStatus[s] ?? 0),
        0,
      ),
    };
    for (const [k, v] of Object.entries(stats.byStatus)) {
      c[k as StatusFilter] = v;
    }
    return c;
  }, [stats]);

  // Actually call the backend DELETE — runs after the 6-second undo
  // grace period unless the user clicks Undo (which clears the timer).
  const flushCancel = useCallback(
    async (id: string) => {
      const pending = pendingCancelsRef.current.get(id);
      if (!pending) return;
      pendingCancelsRef.current.delete(id);
      try {
        await deleteOrder(id, pending.reason);
        // Pull fresh server state — the order is now truly Cancelled,
        // and stats should reflect the bucket move.
        fetchOrders({ silent: true });
        fetchStats();
      } catch (e) {
        // Restore the row if the upstream call ultimately failed.
        setOrders((prev) =>
          prev.find((o) => o.id === id) ? prev : [pending.snapshot, ...prev],
        );
        setTotalCount((n) => n + 1);
        toast.push({
          tone: "error",
          message: `Cancel failed: ${(e as Error).message}`,
        });
      }
    },
    [fetchOrders, fetchStats, toast],
  );

  const undoCancel = useCallback((id: string) => {
    const pending = pendingCancelsRef.current.get(id);
    if (!pending) return;
    clearTimeout(pending.timer);
    pendingCancelsRef.current.delete(id);
    // Insert the snapshot back at its original position if we can; if
    // pagination has shifted, drop it at the top — the next poll will
    // sort everything out.
    setOrders((prev) =>
      prev.find((o) => o.id === id) ? prev : [pending.snapshot, ...prev],
    );
    setTotalCount((n) => n + 1);
  }, []);

  // If the component unmounts (route change, refresh) while cancels are
  // pending, flush them all so the user's intent isn't silently dropped.
  useEffect(() => {
    return () => {
      for (const [id, pending] of pendingCancelsRef.current) {
        clearTimeout(pending.timer);
        // Fire and forget — no error handling possible during unmount.
        void deleteOrder(id, pending.reason).catch(() => {});
      }
      pendingCancelsRef.current.clear();
    };
  }, []);

  // Beforeunload — same idea, but for tab close / hard reload.
  useEffect(() => {
    const handler = () => {
      for (const [id, pending] of pendingCancelsRef.current) {
        clearTimeout(pending.timer);
        // sendBeacon would be ideal but we can't attach the JWT cookie
        // path-restricted shape through it reliably; the regular fetch
        // gets fired and the browser may or may not let it complete.
        // For terminal mutations users dismissed without undo, accept
        // best-effort here.
        void deleteOrder(id, pending.reason).catch(() => {});
      }
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, []);

  // Open the edit dialog for a Draft order. We fetch the full detail
  // (the list DTO drops dimensions/hazmat/etc) so prefill is lossless
  // — without this, saving an edited order would silently wipe those
  // advanced fields. Declared before runAction so it can appear in
  // runAction's dep array without a TDZ violation.
  const openEdit = useCallback(
    async (id: string) => {
      try {
        const detail = await getOrder(id);
        if (detail.orderStatus !== "Draft") {
          toast.push({
            tone: "error",
            message: "Only draft orders can be edited.",
          });
          return;
        }
        setEditingOrder(detail);
      } catch (e) {
        toast.push({
          tone: "error",
          message: `Couldn't load order: ${(e as Error).message}`,
        });
      }
    },
    [toast],
  );

  // Submit/confirm fire immediately; delete routes through the cancel
  // dialog so we can capture the reason the backend's audit log expects.
  // Edit opens the create dialog in edit mode (fetches full detail first).
  const runAction = useCallback(
    async (
      action:
        | "edit"
        | "submit"
        | "confirm"
        | "delete"
        | "reopen"
        | "redispatch"
        | "hold"
        | "release"
        | "reject"
        | "abandon",
      id: string,
      reason?: string,
    ) => {
      const target = orders.find((o) => o.id === id);
      if (!target) return;

      if (action === "edit") {
        void openEdit(id);
        return;
      }

      // Hold / Release / Reject / Abandon — share the StateActionDialog
      // for actor + reason capture. The dialog routes to the right API
      // by variant in its onConfirm handler.
      if (
        action === "hold" ||
        action === "release" ||
        action === "reject" ||
        action === "abandon"
      ) {
        setStateActionTarget({ variant: action, order: target });
        return;
      }

      if (action === "delete" && !reason) {
        setCancelTarget(target);
        // Fire-and-forget query for the cascade callout. Soft-fail —
        // dialog renders without the warning if the count isn't ready.
        const IN_FLIGHT: TripStatus[] = ["Created", "InProgress", "Paused"];
        void getTripsByOrder(target.id)
          .then((trips) => setCancelTripCount(trips.filter((t) => IN_FLIGHT.includes(t.status)).length))
          .catch(() => setCancelTripCount(0));
        return;
      }

      // Reopen is admin-only. Open the dedicated dialog to collect
      // reopenedBy + reason, then the dialog calls the API directly
      // and refreshes the affected row in onConfirm.
      if (action === "reopen") {
        setReopenTarget(target);
        return;
      }

      // Redispatch is for Confirmed orders that never produced a Trip
      // (every group failed dispatch — typical after Reopen + fixing the
      // underlying cause like a missing OrderTemplate). Opens a dialog
      // to collect actor + reason, then POSTs and refreshes.
      if (action === "redispatch") {
        setRedispatchTarget(target);
        return;
      }

      // Cancel uses a 6-second delayed-write so the user can undo. The
      // optimistic UI hides the row immediately; the actual DELETE fires
      // when the timer elapses (or earlier on unmount/unload).
      if (action === "delete") {
        // Drop any prior pending cancel for the same id (re-cancel after
        // undo within the same window).
        const existing = pendingCancelsRef.current.get(id);
        if (existing) clearTimeout(existing.timer);

        const snapshot = target;
        setOrders((prev) => prev.filter((o) => o.id !== id));
        setTotalCount((n) => Math.max(0, n - 1));
        setSelected((prev) => {
          const n = new Set(prev);
          n.delete(id);
          return n;
        });
        if (detailId === id) setDetailId(null);
        setCancelTarget(null);

        const timer = setTimeout(() => void flushCancel(id), 6_000);
        pendingCancelsRef.current.set(id, {
          timer,
          snapshot,
          reason: reason ?? "Cancelled by user.",
        });

        toast.push({
          tone: "info",
          message: `Cancelling ${target.orderRef}…`,
          action: { label: "Undo", onClick: () => undoCancel(id) },
        });
        return;
      }

      const previousOrders = orders;
      const previousTotal = totalCount;

      let nextStatus: OrderStatus | null = null;
      if (action === "submit") nextStatus = "Submitted";
      if (action === "confirm") nextStatus = "Confirmed";
      if (nextStatus) {
        const ns = nextStatus;
        setOrders((prev) => prev.map((o) => (o.id === id ? { ...o, orderStatus: ns } : o)));
      }
      setSelected((prev) => {
        const n = new Set(prev);
        n.delete(id);
        return n;
      });
      setBusy(true);
      try {
        if (action === "submit") await submitOrder(id);
        else await confirmOrder(id);
        toast.push({
          tone: "success",
          message:
            action === "submit"
              ? `Submitted ${target.orderRef}`
              : `Confirmed ${target.orderRef}`,
        });
        setTimeout(() => {
          fetchOrders({ silent: true });
          fetchStats();
        }, 600);
      } catch (e) {
        setOrders(previousOrders);
        setTotalCount(previousTotal);
        toast.push({
          tone: "error",
          message: `${action} failed: ${(e as Error).message}`,
        });
      } finally {
        setBusy(false);
      }
    },
    [
      orders,
      totalCount,
      detailId,
      openEdit,
      flushCancel,
      undoCancel,
      fetchOrders,
      fetchStats,
      toast,
    ],
  );

  // Keep the ref in sync so bulk-cancel can reach the latest runAction
  // closure (deps would otherwise be circular).
  useEffect(() => {
    runActionRef.current = runAction;
  }, [runAction]);

  const handleBulk = useCallback(
    async (action: "submit" | "confirm") => {
      const ids = Array.from(selected);
      if (ids.length === 0) return;
      setBusy(true);
      const results = await Promise.allSettled(
        ids.map((id) => (action === "submit" ? submitOrder(id) : confirmOrder(id))),
      );
      const ok = results.filter((r) => r.status === "fulfilled").length;
      const fail = results.length - ok;
      toast.push({
        tone: fail === 0 ? "success" : fail === ok ? "error" : "info",
        message:
          fail === 0
            ? `${action === "submit" ? "Submitted" : "Confirmed"} ${ok} orders`
            : `${ok} succeeded · ${fail} failed`,
      });
      setSelected(new Set());
      setBusy(false);
      fetchOrders({ silent: true });
      fetchStats();
    },
    [selected, toast, fetchOrders, fetchStats],
  );

  // Bulk cancel — shares the cancel dialog with the single-row path but
  // captures every selected order at once (snapshot taken from current
  // list so the dialog can show "Cancel 5 orders" without an extra
  // fetch). The reason text is shared across all of them; this matches
  // the typical ops scenario (e.g. "shift change, all unscheduled
  // orders are cancelled").
  const handleBulkDelete = useCallback(() => {
    const targets = orders.filter((o) => selected.has(o.id));
    if (targets.length === 0) return;
    setBulkCancelTargets(targets);
  }, [orders, selected]);

  // Fans the confirmed reason out across each selected order using the
  // existing single-cancel delayed-write pipeline (6s timer + undo).
  // Doing this through runAction reuses all the snapshot / restore /
  // beforeunload-flush plumbing for free.
  const runBulkCancel = useCallback(
    (reason: string) => {
      if (!bulkCancelTargets) return;
      const targets = bulkCancelTargets;
      setBulkCancelTargets(null);
      // Issue every cancel through the normal pathway. Each one gets
      // its own pending entry + its own toast — that's intentional so
      // the user can selectively undo a single order without rolling
      // back the whole batch.
      for (const t of targets) {
        void runActionRef.current?.("delete", t.id, reason);
      }
      // Single roll-up toast for the batch context. Individual toasts
      // still show their per-order Undo buttons.
      if (targets.length > 1) {
        toast.push({
          tone: "info",
          message: `Cancelling ${targets.length} orders — undo each individually within 6s`,
        });
      }
    },
    [bulkCancelTargets, toast],
  );

  return (
    <div className="space-y-5 sm:space-y-6">
      {/* Hero / section title */}
      <motion.div
        initial={{ opacity: 0, y: -8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
      >
        <SectionLabel
          icon={<ClipboardList className="h-4 w-4" strokeWidth={2.2} />}
          title="Delivery orders"
          subtitle={
            error
              ? `Couldn't load orders · ${error}`
              : loading
                ? "Loading active and historical orders…"
                : `${stats?.total.toLocaleString("en-US") ?? totalCount.toLocaleString("en-US")} orders · live · refreshes every 15s`
          }
        />
      </motion.div>

      {/* KPI — driven by stats endpoint, system-wide */}
      <OrdersKpiStrip stats={stats} loading={loading && !stats} />

      {/* Filter bar */}
      <FilterBar
        status={statusFilter}
        onStatusChange={setStatusFilter}
        counts={counts}
        search={searchInput}
        onSearchChange={setSearchInput}
        priority={priority}
        onPriorityChange={setPriority}
        transportMode={transportMode}
        onTransportModeChange={setTransportMode}
        hasFailedTrip={hasFailedTrip}
        onHasFailedTripChange={setHasFailedTrip}
        hasActiveJob={hasActiveJob}
        onHasActiveJobChange={setHasActiveJob}
        savedFilterSnapshot={savedFilterSnapshot}
        onApplySavedFilter={applySavedFilter}
        onCreate={() => setCreateOpen(true)}
        onExport={() => {
          if (orders.length === 0) {
            toast.push({ tone: "info", message: "Nothing to export." });
            return;
          }
          exportCsv(orders);
          toast.push({
            tone: "success",
            message: `Exported ${orders.length} order${orders.length === 1 ? "" : "s"} on this page.`,
          });
        }}
        onRefresh={() => {
          fetchOrders();
          fetchStats();
        }}
        refreshing={refreshing}
      />

      {/* Table + Pagination */}
      <div className="space-y-0">
        <OrdersTable
          orders={orders}
          loading={loading}
          selected={selected}
          onSelectionChange={setSelected}
          onOpenDetail={setDetailId}
          onAction={(a, o) => runAction(a, o.id)}
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={handleSortChange}
          search={search}
          hasFilters={
            statusFilter !== "All" ||
            priority !== "All" ||
            transportMode !== "All" ||
            search.trim() !== ""
          }
          onClearFilters={() => {
            setStatusFilter("All");
            setPriority("All");
            setTransportMode("All");
            setSearchInput("");
          }}
        />
        {!loading && totalCount > 0 && (
          <motion.div
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.15 }}
            className="mt-2 rounded-[var(--radius-xl)] glass"
          >
            {paginationMode === "paged" ? (
              <Pagination
                total={totalCount}
                page={page}
                pageSize={pageSize}
                onPageChange={setPage}
                onPageSizeChange={setPageSize}
                mode={paginationMode}
                onModeChange={setPaginationMode}
              />
            ) : (
              <InfiniteFooter
                shown={orders.length}
                total={totalCount}
                loading={refreshing}
                onLoadMore={() => setPage((p) => p + 1)}
                mode={paginationMode}
                onModeChange={setPaginationMode}
              />
            )}
          </motion.div>
        )}
      </div>

      <OrderDetailDrawer
        orderId={detailId}
        onClose={() => setDetailId(null)}
        onOpenOrder={(id) => setDetailId(id)}
        onAction={async (a, id) => {
          await runAction(a, id);
          if (a !== "delete") {
            setTimeout(() => setDetailId(id), 50);
          }
        }}
        onPodScan={(itemId, itemLabel) => {
          if (!detailId) return;
          const order = orders.find((o) => o.id === detailId);
          setPodTarget({
            orderId: detailId,
            orderRef: order?.orderRef ?? "",
            itemId,
            itemLabel,
          });
        }}
      />

      <PodScanDialog
        open={podTarget !== null}
        orderRef={podTarget?.orderRef ?? null}
        itemId={podTarget?.itemId ?? null}
        itemLabel={podTarget?.itemLabel ?? null}
        currentUser={null}
        busy={podBusy}
        error={podError}
        onClose={() => {
          setPodTarget(null);
          setPodError(null);
        }}
        onConfirm={async ({ scannedBy, method, reference, scanType }) => {
          if (!podTarget) return;
          setPodBusy(true);
          setPodError(null);
          try {
            const { confirmItemPod } = await import("@/lib/api/delivery-orders");
            await confirmItemPod(podTarget.orderId, podTarget.itemId, {
              scannedBy, method, reference, scanType,
            });
            toast.push({
              tone: "success",
              message: `${podTarget.itemLabel} POD confirmed`,
            });
            setPodTarget(null);
            // Refresh the drawer so the badge updates immediately
            setTimeout(() => {
              const id = podTarget.orderId;
              setDetailId(null);
              setTimeout(() => setDetailId(id), 30);
            }, 80);
          } catch (err) {
            setPodError((err as Error).message);
          } finally {
            setPodBusy(false);
          }
        }}
      />

      <CreateOrderDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={() => {
          toast.push({ tone: "success", message: "Order created" });
          fetchOrders({ silent: true });
          fetchStats();
        }}
      />

      <CreateOrderDialog
        open={editingOrder !== null}
        editing={editingOrder}
        onClose={() => setEditingOrder(null)}
        onCreated={() => {
          toast.push({ tone: "success", message: "Order updated" });
          fetchOrders({ silent: true });
          fetchStats();
          // If the user had the detail drawer open on this order,
          // re-open it so they see the new values immediately.
          if (editingOrder && detailId === editingOrder.id) {
            setDetailId(null);
            setTimeout(() => setDetailId(editingOrder.id), 60);
          }
        }}
      />

      <CancelOrderDialog
        open={cancelTarget !== null}
        orderRef={cancelTarget?.orderRef ?? null}
        busy={busy}
        activeTripCount={cancelTripCount}
        onClose={() => {
          setCancelTarget(null);
          setCancelTripCount(0);
        }}
        onConfirm={(reason) => {
          if (cancelTarget) runAction("delete", cancelTarget.id, reason);
        }}
      />

      <ReopenDialog
        open={reopenTarget !== null}
        orderRef={reopenTarget?.orderRef ?? null}
        currentUser={null}
        busy={reopenBusy}
        error={reopenError}
        onClose={() => {
          setReopenTarget(null);
          setReopenError(null);
        }}
        onConfirm={async ({ reopenedBy, reason }) => {
          if (!reopenTarget) return;
          setReopenBusy(true);
          setReopenError(null);
          try {
            const res = await fetch(
              `/api/delivery-orders/${reopenTarget.id}/reopen`,
              {
                method: "POST",
                headers: {
                  "Content-Type": "application/json",
                  "Idempotency-Key": crypto.randomUUID(),
                },
                body: JSON.stringify({ reopenedBy, reason }),
              },
            );
            if (!res.ok) {
              const body = (await res.json().catch(() => null)) as {
                message?: string;
              } | null;
              throw new Error(body?.message ?? `Reopen failed (${res.status})`);
            }
            // Optimistic: bump the row to Confirmed so the list reflects
            // the new state immediately. Next poll will reconcile.
            setOrders((prev) =>
              prev.map((o) =>
                o.id === reopenTarget.id ? { ...o, orderStatus: "Confirmed" } : o,
              ),
            );
            toast.push({
              tone: "success",
              message: `${reopenTarget.orderRef} reopened — use Retry on the failed Trip to redispatch.`,
            });
            setReopenTarget(null);
          } catch (err) {
            setReopenError((err as Error).message);
          } finally {
            setReopenBusy(false);
          }
        }}
      />

      <RedispatchDialog
        open={redispatchTarget !== null}
        orderRef={redispatchTarget?.orderRef ?? null}
        currentUser={null}
        busy={redispatchBusy}
        error={redispatchError}
        onClose={() => {
          setRedispatchTarget(null);
          setRedispatchError(null);
        }}
        onConfirm={async ({ redispatchedBy, reason }) => {
          if (!redispatchTarget) return;
          setRedispatchBusy(true);
          setRedispatchError(null);
          try {
            const res = await fetch(
              `/api/delivery-orders/${redispatchTarget.id}/redispatch`,
              {
                method: "POST",
                headers: {
                  "Content-Type": "application/json",
                  "Idempotency-Key": crypto.randomUUID(),
                },
                body: JSON.stringify({ redispatchedBy, reason }),
              },
            );
            if (!res.ok) {
              const body = (await res.json().catch(() => null)) as {
                message?: string;
              } | null;
              throw new Error(body?.message ?? `Redispatch failed (${res.status})`);
            }
            toast.push({
              tone: "success",
              message: `${redispatchTarget.orderRef} dispatch re-queued — Planning will run again.`,
            });
            setRedispatchTarget(null);
          } catch (err) {
            setRedispatchError((err as Error).message);
          } finally {
            setRedispatchBusy(false);
          }
        }}
      />

      <StateActionDialog
        open={stateActionTarget !== null}
        variant={stateActionTarget?.variant ?? null}
        orderRef={stateActionTarget?.order.orderRef ?? null}
        currentUser={null}
        busy={stateActionBusy}
        error={stateActionError}
        onClose={() => {
          setStateActionTarget(null);
          setStateActionError(null);
        }}
        onConfirm={async ({ actor, reason }) => {
          if (!stateActionTarget) return;
          const { variant, order } = stateActionTarget;
          setStateActionBusy(true);
          setStateActionError(null);
          try {
            if (variant === "hold") {
              await holdOrder(order.id, { reason, heldBy: actor || undefined });
              setOrders((prev) =>
                prev.map((o) =>
                  o.id === order.id ? { ...o, orderStatus: "Held" } : o,
                ),
              );
              toast.push({ tone: "info", message: `${order.orderRef} held` });
            } else if (variant === "release") {
              await releaseOrder(order.id, { releasedBy: actor || undefined });
              setOrders((prev) =>
                prev.map((o) =>
                  o.id === order.id ? { ...o, orderStatus: "Confirmed" } : o,
                ),
              );
              toast.push({
                tone: "success",
                message: `${order.orderRef} released — Planning will re-run`,
              });
            } else if (variant === "reject") {
              await rejectOrder(order.id, { reason, rejectedBy: actor || undefined });
              setOrders((prev) =>
                prev.map((o) =>
                  o.id === order.id ? { ...o, orderStatus: "Rejected" } : o,
                ),
              );
              toast.push({ tone: "info", message: `${order.orderRef} rejected` });
            } else {
              // variant === "abandon" — Phase b11 stuck-order close-out.
              await abandonStuckOrder(order.id, { abandonedBy: actor, reason });
              setOrders((prev) =>
                prev.map((o) =>
                  o.id === order.id ? { ...o, orderStatus: "Cancelled" } : o,
                ),
              );
              if (detailId === order.id) setDetailId(null);
              toast.push({
                tone: "info",
                message: `${order.orderRef} abandoned — items terminated`,
              });
            }
            setStateActionTarget(null);
            setTimeout(() => {
              fetchOrders({ silent: true });
              fetchStats();
            }, 600);
          } catch (err) {
            setStateActionError((err as Error).message);
          } finally {
            setStateActionBusy(false);
          }
        }}
      />

      <BulkActionBar
        count={selected.size}
        onClear={() => setSelected(new Set())}
        onSubmitAll={() => handleBulk("submit")}
        onConfirmAll={() => handleBulk("confirm")}
        onDeleteAll={handleBulkDelete}
        busy={busy}
      />

      <CancelOrderDialog
        open={bulkCancelTargets !== null}
        orderRef={
          bulkCancelTargets ? `${bulkCancelTargets.length} orders` : null
        }
        count={bulkCancelTargets?.length ?? 0}
        busy={busy}
        onClose={() => setBulkCancelTargets(null)}
        onConfirm={runBulkCancel}
      />
    </div>
  );
}

export function OrdersExperience() {
  return (
    <ToastProvider>
      <ExperienceInner />
    </ToastProvider>
  );
}
