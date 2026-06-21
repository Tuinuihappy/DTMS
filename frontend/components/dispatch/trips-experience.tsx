"use client";

import {
  ChevronDown,
  Filter,
  Loader2,
  RefreshCw,
  Route,
  Search,
  X,
} from "lucide-react";
import { motion } from "motion/react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  listTrips,
  type TripQueueItemDto,
  type TripQueueSortKey,
  type TripStatus,
} from "@/lib/api/trips";
import { cn } from "@/lib/utils";
import { fromDateTimeLocalInput } from "@/lib/datetime";
import { DateTime } from "@/components/primitives/date-time";
import { Pagination, type PageSize } from "@/components/delivery-orders/pagination";
import { TripStatusBadge, AttemptBadge } from "./badges";
import { TripDetailDrawer } from "./trip-detail-drawer";

// ── Constants ──────────────────────────────────────────────────────────

type StatusFilter = "All" | "Active" | "Terminal" | TripStatus;

const STATUS_FILTERS: StatusFilter[] = [
  "All",
  "Active",
  "Terminal",
  "Created",
  "InProgress",
  "Paused",
  "Completed",
  "Failed",
  "Cancelled",
];

// Map UI filter buckets into the concrete TripStatus list the backend
// understands. "Active" = in-flight (Created/InProgress/Paused);
// "Terminal" = finished (Completed/Failed/Cancelled); "All" = no filter.
function filterToStatuses(f: StatusFilter): TripStatus[] | undefined {
  if (f === "All") return undefined;
  if (f === "Active") return ["Created", "InProgress", "Paused"];
  if (f === "Terminal") return ["Completed", "Failed", "Cancelled"];
  return [f];
}

const STATUS_FILTER_LABEL: Record<StatusFilter, string> = {
  All: "All",
  Active: "Active",
  Terminal: "Terminal",
  Created: "Created",
  InProgress: "In progress",
  Paused: "Paused",
  Completed: "Completed",
  Failed: "Failed",
  Cancelled: "Cancelled",
};

const SORT_OPTIONS: { value: TripQueueSortKey; label: string }[] = [
  { value: "createdAt", label: "Created" },
  { value: "startedAt", label: "Started" },
  { value: "completedAt", label: "Completed" },
  { value: "attemptNumber", label: "Attempt" },
  { value: "status", label: "Status" },
  { value: "priority", label: "Priority" },
];

const PAGE_SIZE_VALUES: PageSize[] = [10, 25, 50, 100];

function useDebouncedValue<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

// ── Component ──────────────────────────────────────────────────────────

export function TripsExperience() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const [statusFilter, setStatusFilter] = useState<StatusFilter>(() => {
    const raw = searchParams.get("status") as StatusFilter | null;
    return raw && STATUS_FILTERS.includes(raw) ? raw : "All";
  });
  const [searchInput, setSearchInput] = useState(() => searchParams.get("q") ?? "");
  const search = useDebouncedValue(searchInput, 350);
  const [vehicleKeyInput, setVehicleKeyInput] = useState(
    () => searchParams.get("vehicle") ?? "",
  );
  const vehicleKey = useDebouncedValue(vehicleKeyInput, 350);
  const [fromDate, setFromDate] = useState(() => searchParams.get("from") ?? "");
  const [toDate, setToDate] = useState(() => searchParams.get("to") ?? "");

  const [page, setPage] = useState(() => {
    const n = Number(searchParams.get("page"));
    return Number.isFinite(n) && n > 0 ? Math.floor(n) : 1;
  });
  const [pageSize, setPageSize] = useState<PageSize>(() => {
    const n = Number(searchParams.get("size")) as PageSize;
    return PAGE_SIZE_VALUES.includes(n) ? n : 25;
  });
  const [sortBy, setSortBy] = useState<TripQueueSortKey>(() => {
    const raw = searchParams.get("sortBy") as TripQueueSortKey | null;
    return raw && SORT_OPTIONS.some((o) => o.value === raw) ? raw : "createdAt";
  });
  const [sortDir, setSortDir] = useState<"asc" | "desc">(() =>
    searchParams.get("sortDir") === "asc" ? "asc" : "desc",
  );

  const [trips, setTrips] = useState<TripQueueItemDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailTripId, setDetailTripId] = useState<string | null>(null);

  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const fetchAbortRef = useRef<AbortController | null>(null);

  // Reset to page 1 when filters change.
  useEffect(() => {
    setPage(1);
  }, [statusFilter, search, vehicleKey, fromDate, toDate, pageSize]);

  // Mirror state back to URL so refresh/back/forward and deep links work.
  useEffect(() => {
    const p = new URLSearchParams();
    if (statusFilter !== "All") p.set("status", statusFilter);
    if (search) p.set("q", search);
    if (vehicleKey) p.set("vehicle", vehicleKey);
    if (fromDate) p.set("from", fromDate);
    if (toDate) p.set("to", toDate);
    if (page > 1) p.set("page", String(page));
    if (pageSize !== 25) p.set("size", String(pageSize));
    if (sortBy !== "createdAt") p.set("sortBy", sortBy);
    if (sortDir !== "desc") p.set("sortDir", sortDir);
    const qs = p.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }, [statusFilter, search, vehicleKey, fromDate, toDate, page, pageSize, sortBy, sortDir, router, pathname]);

  const fetchTrips = useCallback(
    async (opts?: { silent?: boolean }) => {
      fetchAbortRef.current?.abort();
      const controller = new AbortController();
      fetchAbortRef.current = controller;
      if (!opts?.silent) setRefreshing(true);
      setError(null);
      try {
        const res = await listTrips(
          {
            statuses: filterToStatuses(statusFilter),
            search: search.trim() || undefined,
            vehicleKey: vehicleKey.trim() || undefined,
            // Treat date inputs as start/end of day in the user's local tz,
            // then send as ISO. Backend stores CreatedAt in UTC.
            fromUtc: fromDateTimeLocalInput(fromDate ? `${fromDate}T00:00:00` : "") ?? undefined,
            toUtc: fromDateTimeLocalInput(toDate ? `${toDate}T23:59:59` : "") ?? undefined,
            sortBy,
            sortDir,
            page,
            pageSize,
          },
          controller.signal,
        );
        setTrips(res.items);
        setTotalCount(res.totalCount);
      } catch (e) {
        if ((e as Error).name === "AbortError") return;
        setError((e as Error).message || "Failed to load trips.");
      } finally {
        if (fetchAbortRef.current === controller) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [statusFilter, search, vehicleKey, fromDate, toDate, sortBy, sortDir, page, pageSize],
  );

  useEffect(() => {
    fetchTrips();
  }, [fetchTrips]);

  // Soft polling — 15s while the drawer is closed and the tab is visible.
  useEffect(() => {
    if (detailTripId) return;
    const start = () => {
      if (pollRef.current) return;
      pollRef.current = setInterval(() => fetchTrips({ silent: true }), 15_000);
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
        fetchTrips({ silent: true });
      }
    };
    if (!document.hidden) start();
    document.addEventListener("visibilitychange", handleVisibility);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibility);
      stop();
    };
  }, [detailTripId, fetchTrips]);

  const handleSortHeader = useCallback(
    (col: TripQueueSortKey) => {
      if (col === sortBy) {
        setSortDir((d) => (d === "asc" ? "desc" : "asc"));
      } else {
        setSortBy(col);
        setSortDir("desc");
      }
    },
    [sortBy],
  );

  const clearFilters = () => {
    setStatusFilter("All");
    setSearchInput("");
    setVehicleKeyInput("");
    setFromDate("");
    setToDate("");
  };

  const hasFilters =
    statusFilter !== "All" || !!search || !!vehicleKey || !!fromDate || !!toDate;

  return (
    <div className="flex flex-col gap-4">
      <Header
        total={totalCount}
        refreshing={refreshing}
        onRefresh={() => fetchTrips()}
      />

      <FiltersBar
        statusFilter={statusFilter}
        onStatusFilter={setStatusFilter}
        searchInput={searchInput}
        onSearchInput={setSearchInput}
        vehicleKeyInput={vehicleKeyInput}
        onVehicleKeyInput={setVehicleKeyInput}
        fromDate={fromDate}
        onFromDate={setFromDate}
        toDate={toDate}
        onToDate={setToDate}
        sortBy={sortBy}
        sortDir={sortDir}
        onSortBy={setSortBy}
        onSortDir={setSortDir}
        hasFilters={hasFilters}
        onClear={clearFilters}
      />

      <TripsTable
        trips={trips}
        loading={loading}
        error={error}
        sortBy={sortBy}
        sortDir={sortDir}
        onSortHeader={handleSortHeader}
        onOpenTrip={setDetailTripId}
      />

      <Pagination
        total={totalCount}
        page={page}
        pageSize={pageSize}
        onPageChange={setPage}
        onPageSizeChange={(s) => setPageSize(s)}
      />

      <TripDetailDrawer
        tripId={detailTripId}
        onClose={() => setDetailTripId(null)}
        onOpenTrip={(id) => setDetailTripId(id)}
      />
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────

function Header({
  total,
  refreshing,
  onRefresh,
}: {
  total: number;
  refreshing: boolean;
  onRefresh: () => void;
}) {
  return (
    <div className="flex items-center justify-between gap-3 px-1">
      <div className="flex items-center gap-3">
        <span className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-[var(--color-pastel-peach)] to-[#fcb98a] text-[var(--color-pastel-peach-ink)] shadow-[inset_0_1px_0_rgba(255,255,255,0.6)]">
          <Route className="h-4 w-4" strokeWidth={2.2} />
        </span>
        <div>
          <h1 className="font-display text-[22px] font-semibold tracking-tight text-[var(--color-ink-900)]">
            Trips
          </h1>
          <p className="text-[12px] text-[var(--color-ink-500)]">
            Envelope dispatches across every order — search, filter, drill.
            <span className="mx-2 font-mono tabular-nums text-[var(--color-ink-400)]">
              {total.toLocaleString()} total
            </span>
          </p>
        </div>
      </div>
      <button
        type="button"
        onClick={onRefresh}
        className={cn(
          "inline-flex h-9 items-center gap-1.5 rounded-full bg-white/70 px-3 text-[12px] font-medium text-[var(--color-ink-700)] backdrop-blur-md transition-colors hover:bg-white",
          "border border-white/70 dark:bg-white/[0.06] dark:border-white/10 dark:hover:bg-white/[0.12]",
        )}
      >
        <RefreshCw className={cn("h-3.5 w-3.5", refreshing && "animate-spin")} strokeWidth={2.2} />
        Refresh
      </button>
    </div>
  );
}

function FiltersBar(props: {
  statusFilter: StatusFilter;
  onStatusFilter: (f: StatusFilter) => void;
  searchInput: string;
  onSearchInput: (s: string) => void;
  vehicleKeyInput: string;
  onVehicleKeyInput: (s: string) => void;
  fromDate: string;
  onFromDate: (s: string) => void;
  toDate: string;
  onToDate: (s: string) => void;
  sortBy: TripQueueSortKey;
  sortDir: "asc" | "desc";
  onSortBy: (s: TripQueueSortKey) => void;
  onSortDir: (d: "asc" | "desc") => void;
  hasFilters: boolean;
  onClear: () => void;
}) {
  return (
    <div className="rounded-2xl bg-white/60 p-3 backdrop-blur-md border border-white/70 dark:bg-white/[0.04] dark:border-white/10">
      {/* Status chips */}
      <div className="flex flex-wrap items-center gap-1.5 pb-3">
        <span className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)] pr-1">
          Status
        </span>
        {STATUS_FILTERS.map((f) => (
          <button
            key={f}
            type="button"
            onClick={() => props.onStatusFilter(f)}
            className={cn(
              "inline-flex h-7 items-center rounded-full px-2.5 text-[11.5px] font-semibold transition-colors",
              props.statusFilter === f
                ? "bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_6px_14px_-6px_rgba(14,21,48,0.5)] dark:bg-[var(--color-brand-500)]"
                : "bg-white/70 text-[var(--color-ink-700)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.1]",
            )}
          >
            {STATUS_FILTER_LABEL[f]}
          </button>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {/* Search */}
        <div className="relative flex-1 min-w-[200px]">
          <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)]" strokeWidth={2.2} />
          <input
            type="text"
            value={props.searchInput}
            onChange={(e) => props.onSearchInput(e.target.value)}
            placeholder="UpperKey, vendor order key, order ref…"
            className={cn(
              "h-9 w-full rounded-full bg-white/80 pl-8 pr-3 text-[12.5px] text-[var(--color-ink-900)]",
              "border border-white/70 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40",
              "dark:bg-white/[0.05] dark:border-white/10 dark:text-white placeholder:text-[var(--color-ink-400)]",
            )}
          />
        </div>

        {/* Vehicle key */}
        <input
          type="text"
          value={props.vehicleKeyInput}
          onChange={(e) => props.onVehicleKeyInput(e.target.value)}
          placeholder="Vehicle key (e.g. Delta6FAN1)"
          className={cn(
            "h-9 w-[200px] rounded-full bg-white/80 px-3 text-[12.5px] text-[var(--color-ink-900)]",
            "border border-white/70 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40",
            "dark:bg-white/[0.05] dark:border-white/10 dark:text-white placeholder:text-[var(--color-ink-400)]",
          )}
        />

        {/* Date range */}
        <div className="flex items-center gap-1.5 rounded-full bg-white/80 px-3 py-1 border border-white/70 dark:bg-white/[0.05] dark:border-white/10">
          <span className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
            Created
          </span>
          <input
            type="date"
            value={props.fromDate}
            onChange={(e) => props.onFromDate(e.target.value)}
            className="bg-transparent text-[12px] text-[var(--color-ink-900)] focus:outline-none dark:text-white"
          />
          <span className="text-[var(--color-ink-300)]">→</span>
          <input
            type="date"
            value={props.toDate}
            onChange={(e) => props.onToDate(e.target.value)}
            className="bg-transparent text-[12px] text-[var(--color-ink-900)] focus:outline-none dark:text-white"
          />
        </div>

        {/* Sort */}
        <div className="flex items-center gap-1 rounded-full bg-white/80 pl-3 pr-1 py-1 border border-white/70 dark:bg-white/[0.05] dark:border-white/10">
          <span className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
            Sort
          </span>
          <select
            value={props.sortBy}
            onChange={(e) => props.onSortBy(e.target.value as TripQueueSortKey)}
            className="bg-transparent text-[12px] font-semibold text-[var(--color-ink-900)] focus:outline-none dark:text-white"
          >
            {SORT_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={() => props.onSortDir(props.sortDir === "asc" ? "desc" : "asc")}
            className="grid h-7 w-7 place-items-center rounded-full text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] dark:hover:bg-white/[0.06]"
            title={props.sortDir === "asc" ? "Ascending" : "Descending"}
          >
            <ChevronDown
              className={cn("h-3.5 w-3.5 transition-transform", props.sortDir === "asc" && "rotate-180")}
              strokeWidth={2.4}
            />
          </button>
        </div>

        {props.hasFilters && (
          <button
            type="button"
            onClick={props.onClear}
            className="inline-flex h-9 items-center gap-1.5 rounded-full px-3 text-[11.5px] font-medium text-[var(--color-coral)] hover:bg-[#fde0db]/60"
          >
            <X className="h-3 w-3" strokeWidth={2.4} />
            Clear filters
          </button>
        )}
      </div>
    </div>
  );
}

function TripsTable({
  trips,
  loading,
  error,
  sortBy,
  sortDir,
  onSortHeader,
  onOpenTrip,
}: {
  trips: TripQueueItemDto[];
  loading: boolean;
  error: string | null;
  sortBy: TripQueueSortKey;
  sortDir: "asc" | "desc";
  onSortHeader: (col: TripQueueSortKey) => void;
  onOpenTrip: (id: string) => void;
}) {
  if (loading && trips.length === 0) {
    return (
      <div className="rounded-2xl bg-white/60 p-12 text-center backdrop-blur-md border border-white/70 dark:bg-white/[0.04] dark:border-white/10">
        <Loader2 className="mx-auto h-5 w-5 animate-spin text-[var(--color-ink-400)]" />
        <p className="mt-2 text-[12px] text-[var(--color-ink-500)]">Loading trips…</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-2xl bg-[#fde0db]/40 p-6 text-center backdrop-blur-md border border-[var(--color-coral)]/20">
        <p className="text-[13px] font-semibold text-[var(--color-coral)]">{error}</p>
      </div>
    );
  }

  if (trips.length === 0) {
    return (
      <div className="rounded-2xl bg-white/60 p-12 text-center backdrop-blur-md border border-white/70 dark:bg-white/[0.04] dark:border-white/10">
        <Filter className="mx-auto h-6 w-6 text-[var(--color-ink-300)]" strokeWidth={1.8} />
        <p className="mt-3 text-[13.5px] font-semibold text-[var(--color-ink-700)]">
          No trips match your filters
        </p>
        <p className="mt-1 text-[12px] text-[var(--color-ink-500)]">
          Try widening the date range, clearing the search, or switching to All.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-2xl bg-white/60 backdrop-blur-md border border-white/70 dark:bg-white/[0.04] dark:border-white/10">
      <table className="w-full text-left text-[12.5px]">
        <thead className="bg-white/40 dark:bg-white/[0.02]">
          <tr className="text-[10.5px] font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-500)]">
            <Th>Status</Th>
            <Th>Order ref</Th>
            <SortableTh col="attemptNumber" sortBy={sortBy} sortDir={sortDir} onClick={onSortHeader}>
              Attempt
            </SortableTh>
            <Th>Upper key</Th>
            <Th>Vehicle</Th>
            <Th>Template</Th>
            <SortableTh col="priority" sortBy={sortBy} sortDir={sortDir} onClick={onSortHeader}>
              Priority
            </SortableTh>
            <SortableTh col="createdAt" sortBy={sortBy} sortDir={sortDir} onClick={onSortHeader}>
              Created
            </SortableTh>
            <SortableTh col="startedAt" sortBy={sortBy} sortDir={sortDir} onClick={onSortHeader}>
              Started
            </SortableTh>
            <SortableTh col="completedAt" sortBy={sortBy} sortDir={sortDir} onClick={onSortHeader}>
              Completed
            </SortableTh>
          </tr>
        </thead>
        <tbody>
          {trips.map((t) => (
            <tr
              key={t.id}
              onClick={() => onOpenTrip(t.id)}
              className="border-t border-white/30 transition-colors hover:bg-white/40 cursor-pointer dark:border-white/[0.04] dark:hover:bg-white/[0.04]"
            >
              <Td>
                <TripStatusBadge status={t.status} />
              </Td>
              <Td>
                {t.orderRef ? (
                  <span className="font-mono text-[12px] font-semibold text-[var(--color-ink-900)] dark:text-white">
                    {t.orderRef}
                  </span>
                ) : (
                  <span className="text-[var(--color-ink-300)]">—</span>
                )}
              </Td>
              <Td>
                <AttemptBadge attempt={t.attemptNumber} />
              </Td>
              <Td className="max-w-[180px] truncate">
                <span className="font-mono text-[11.5px] text-[var(--color-ink-700)] dark:text-[var(--color-ink-500)]">
                  {t.upperKey}
                </span>
              </Td>
              <Td className="max-w-[180px]">
                {t.vendorVehicleName ? (
                  <div className="truncate">
                    <div
                      className="truncate text-[11.5px] font-semibold text-[var(--color-ink-900)] dark:text-white"
                      title={t.vendorVehicleKey ?? undefined}
                    >
                      {t.vendorVehicleName}
                    </div>
                    {t.vendorVehicleKey && (
                      <div
                        className="truncate font-mono text-[10px] text-[var(--color-ink-400)]"
                        title={t.vendorVehicleKey}
                      >
                        {t.vendorVehicleKey}
                      </div>
                    )}
                  </div>
                ) : t.vendorVehicleKey ? (
                  <span
                    className="font-mono text-[11.5px] text-[var(--color-ink-700)] dark:text-[var(--color-ink-500)]"
                    title={t.vendorVehicleKey}
                  >
                    {t.vendorVehicleKey}
                  </span>
                ) : (
                  <span className="text-[var(--color-ink-300)]">—</span>
                )}
              </Td>
              <Td className="max-w-[160px] truncate">
                {t.templateNameAtDispatch ?? (
                  <span className="text-[var(--color-ink-300)]">—</span>
                )}
              </Td>
              <Td>
                {t.priorityAtDispatch != null ? (
                  <span className="font-mono tabular-nums text-[11.5px] font-semibold text-[var(--color-ink-700)] dark:text-[var(--color-ink-500)]">
                    {t.priorityAtDispatch}
                  </span>
                ) : (
                  <span className="text-[var(--color-ink-300)]">—</span>
                )}
              </Td>
              <Td>
                <DateTime
                  value={t.createdAt}
                  className="font-mono text-[11px] tabular-nums text-[var(--color-ink-600)] dark:text-[var(--color-ink-500)]"
                />
              </Td>
              <Td>
                <DateTime
                  value={t.startedAt}
                  className="font-mono text-[11px] tabular-nums text-[var(--color-ink-600)] dark:text-[var(--color-ink-500)]"
                />
              </Td>
              <Td>
                <DateTime
                  value={t.completedAt}
                  className="font-mono text-[11px] tabular-nums text-[var(--color-ink-600)] dark:text-[var(--color-ink-500)]"
                />
              </Td>
            </tr>
          ))}
        </tbody>
      </table>

      {trips.some((t) => t.failureReason) && (
        <div className="border-t border-white/30 px-4 py-2 dark:border-white/[0.04]">
          <span className="text-[10.5px] uppercase tracking-[0.1em] text-[var(--color-coral)]">
            Some trips failed — click the row to see the reason.
          </span>
        </div>
      )}
    </div>
  );
}

function Th({ children, className }: { children: React.ReactNode; className?: string }) {
  return <th className={cn("px-3 py-2.5", className)}>{children}</th>;
}

function SortableTh({
  col,
  sortBy,
  sortDir,
  onClick,
  children,
}: {
  col: TripQueueSortKey;
  sortBy: TripQueueSortKey;
  sortDir: "asc" | "desc";
  onClick: (c: TripQueueSortKey) => void;
  children: React.ReactNode;
}) {
  const active = sortBy === col;
  return (
    <th className="px-3 py-2.5">
      <button
        type="button"
        onClick={() => onClick(col)}
        className={cn(
          "inline-flex items-center gap-1 transition-colors",
          active ? "text-[var(--color-ink-900)] dark:text-white" : "hover:text-[var(--color-ink-700)]",
        )}
      >
        {children}
        {active && (
          <ChevronDown
            className={cn("h-3 w-3 transition-transform", sortDir === "asc" && "rotate-180")}
            strokeWidth={2.4}
          />
        )}
      </button>
    </th>
  );
}

function Td({ children, className }: { children: React.ReactNode; className?: string }) {
  return <td className={cn("px-3 py-2.5", className)}>{children}</td>;
}

