"use client";

import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  AlertTriangle,
  CircleSlash,
  FileStack,
  Layers,
  Plus,
  Power,
  RefreshCw,
  Rocket,
  Route,
  Search,
  Sparkles,
  Trash2,
  Truck,
  X,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { SectionLabel } from "@/components/primitives/section-label";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";
import { Pagination, type PageSize } from "@/components/delivery-orders/pagination";
import {
  activateOrderTemplate,
  deactivateOrderTemplate,
  deleteOrderTemplate,
  deriveStats,
  listOrderTemplates,
  type OrderTemplateDto,
  type OrderTemplateStats,
} from "@/lib/api/order-templates";
import { OrderTemplatesTable, type SortColumn, type SortDir } from "./order-templates-table";
import { OrderTemplateDrawer } from "./detail-drawer";
import { CreateFromTemplateDialog } from "./create-from-template-dialog";

type StatusFilter = "All" | "Active" | "Inactive";

const KPI_TONES = {
  brand:
    "from-[var(--color-pastel-sky)] to-[var(--color-pastel-sky-tail)] text-[var(--color-brand-900)] dark:text-[var(--color-pastel-sky-ink)]",
  success:
    "from-[var(--color-success-soft)] to-[var(--color-pastel-mint-tail)] text-[var(--color-success)] dark:text-[var(--color-success)]",
  lavender:
    "from-[var(--color-pastel-lavender)] to-[var(--color-pastel-lavender-tail)] text-[var(--color-pastel-lavender-ink)] dark:text-[var(--color-pastel-lavender-ink)]",
  peach:
    "from-[var(--color-pastel-peach)] to-[var(--color-pastel-peach-tail)] text-[var(--color-pastel-peach-ink)] dark:text-[var(--color-pastel-peach-ink)]",
} as const;

const EMPTY_STATS: OrderTemplateStats = {
  total: 0,
  active: 0,
  inactive: 0,
  avgMissions: 0,
  withVehicleBinding: 0,
};

function useDebouncedValue<T>(value: T, delay = 250): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

export function OrderTemplatesExperience() {
  // The backend list endpoint doesn't support search/sort yet, so we
  // fetch the full catalog (size up to backend's 200 max) and do all
  // filtering / sorting / paging in-memory. Catalogue is small enough.
  const [allRecords, setAllRecords] = useState<OrderTemplateDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search, 250);
  const [sortBy, setSortBy] = useState<SortColumn>("modifiedAt");
  const [sortDir, setSortDir] = useState<SortDir>("desc");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(10);

  const [selected, setSelected] = useState<OrderTemplateDto | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<OrderTemplateDto | null>(null);
  const [dispatching, setDispatching] = useState<OrderTemplateDto | null>(null);

  const [toast, setToast] = useState<{ kind: "ok" | "err"; msg: string } | null>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const flash = useCallback((kind: "ok" | "err", msg: string) => {
    setToast({ kind, msg });
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(null), 3200);
  }, []);

  useEffect(() => {
    setPage(1);
  }, [statusFilter, debouncedSearch, pageSize, sortBy, sortDir]);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const result = await listOrderTemplates(
        { page: 1, size: 200, includeInactive: true },
        signal,
      );
      setAllRecords(result.records ?? []);
    } catch (e) {
      if ((e as Error).name === "AbortError") return;
      setError((e as Error).message || "Failed to load templates.");
      setAllRecords([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const ac = new AbortController();
    void refresh(ac.signal);
    return () => ac.abort();
  }, [refresh]);

  // Derived: stats from full set; filtered/sorted/paged view from filters.
  const stats = useMemo<OrderTemplateStats>(
    () => deriveStats(allRecords),
    [allRecords],
  );

  const filteredSorted = useMemo(() => {
    const q = debouncedSearch.trim().toLowerCase();
    let rows = allRecords;
    if (statusFilter === "Active") rows = rows.filter((r) => r.isActive);
    else if (statusFilter === "Inactive") rows = rows.filter((r) => !r.isActive);
    if (q) {
      rows = rows.filter(
        (r) =>
          r.name.toLowerCase().includes(q) ||
          (r.description ?? "").toLowerCase().includes(q) ||
          (r.appointVehicleName ?? "").toLowerCase().includes(q) ||
          (r.appointVehicleGroupName ?? "").toLowerCase().includes(q) ||
          (r.transportOrder?.missions ?? []).some((m) =>
            (m.actionTemplateName ?? "").toLowerCase().includes(q),
          ),
      );
    }
    const dir = sortDir === "asc" ? 1 : -1;
    const sorted = [...rows].sort((a, b) => {
      switch (sortBy) {
        case "name":
          return a.name.localeCompare(b.name) * dir;
        case "priority":
          return (a.priority - b.priority) * dir;
        case "missions":
          return (
            ((a.transportOrder?.missions?.length ?? 0) -
              (b.transportOrder?.missions?.length ?? 0)) *
            dir
          );
        case "isActive":
          return (Number(a.isActive) - Number(b.isActive)) * dir;
        case "modifiedAt":
        default: {
          const at = new Date(a.modifiedAt ?? a.createdAt).getTime();
          const bt = new Date(b.modifiedAt ?? b.createdAt).getTime();
          return (at - bt) * dir;
        }
      }
    });
    return sorted;
  }, [allRecords, statusFilter, debouncedSearch, sortBy, sortDir]);

  const totalCount = filteredSorted.length;
  const pageStart = (page - 1) * pageSize;
  const paged = useMemo(
    () => filteredSorted.slice(pageStart, pageStart + pageSize),
    [filteredSorted, pageStart, pageSize],
  );

  useEffect(() => {
    const lastPage = Math.max(1, Math.ceil(totalCount / pageSize));
    if (page > lastPage) setPage(lastPage);
  }, [totalCount, page, pageSize]);

  const hasFilters = statusFilter !== "All" || debouncedSearch.trim().length > 0;
  const clearFilters = () => {
    setStatusFilter("All");
    setSearch("");
  };

  const handleSortChange = (col: SortColumn) => {
    if (sortBy === col) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else {
      setSortBy(col);
      setSortDir(col === "name" ? "asc" : "desc");
    }
  };

  async function handleToggleActive(t: OrderTemplateDto) {
    setBusyId(t.id);
    try {
      if (t.isActive) await deactivateOrderTemplate(t.id);
      else await activateOrderTemplate(t.id);
      await refresh();
      setSelected((cur) => (cur?.id === t.id ? { ...cur, isActive: !t.isActive } : cur));
      flash("ok", t.isActive ? "Template deactivated." : "Template activated.");
    } catch (e) {
      flash("err", (e as Error).message || "Action failed.");
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(t: OrderTemplateDto) {
    setBusyId(t.id);
    try {
      await deleteOrderTemplate(t.id);
      await refresh();
      setSelected((cur) => (cur?.id === t.id ? null : cur));
      flash("ok", `Deleted "${t.name}".`);
    } catch (e) {
      flash("err", (e as Error).message || "Delete failed.");
    } finally {
      setBusyId(null);
      setConfirmDelete(null);
    }
  }

  return (
    <div className="space-y-7">
      {/* Header */}
      <SectionLabel
        icon={<FileStack className="h-4 w-4" strokeWidth={2.1} />}
        title="Order templates"
        subtitle="Reusable order recipes — author once, dispatch in seconds. Missions compose Action templates by reference, so changes propagate automatically."
        action={
          <Link
            href="/delivery-orders/order-templates/new"
            className="group inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12.5px] font-semibold text-white shadow-[0_6px_22px_-8px_rgba(15,23,42,0.55)] transition-all hover:-translate-y-0.5 hover:shadow-[0_8px_28px_-8px_rgba(15,23,42,0.65)] dark:bg-[var(--color-brand-500)]"
          >
            <Plus className="h-3.5 w-3.5 transition-transform group-hover:rotate-90" strokeWidth={2.4} />
            New template
          </Link>
        }
      />

      {/* Hero band — frames the "Create order from template" value prop */}
      <HeroBand
        active={stats.active}
        avgMissions={stats.avgMissions}
        templates={allRecords}
        onDispatch={(t) => setDispatching(t)}
      />

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
        <KpiTile
          icon={<Layers className="h-4 w-4" strokeWidth={2.2} />}
          label="Total templates"
          value={stats.total}
          tone="brand"
          delay={0}
          loading={loading && allRecords.length === 0}
        />
        <KpiTile
          icon={<Power className="h-4 w-4" strokeWidth={2.2} />}
          label="Active"
          value={stats.active}
          tone="success"
          delay={0.05}
          loading={loading && allRecords.length === 0}
          live={stats.active > 0 && !loading}
        />
        <KpiTile
          icon={<Route className="h-4 w-4" strokeWidth={2.2} />}
          label="Avg missions"
          value={stats.avgMissions}
          decimals={1}
          tone="lavender"
          delay={0.1}
          loading={loading && allRecords.length === 0}
        />
        <KpiTile
          icon={<Truck className="h-4 w-4" strokeWidth={2.2} />}
          label="Vehicle-bound"
          value={stats.withVehicleBinding}
          tone="peach"
          delay={0.15}
          loading={loading && allRecords.length === 0}
        />
      </div>

      {/* Filter bar */}
      <motion.div
        layout
        className="glass flex flex-wrap items-center gap-2 rounded-[var(--radius)] px-3 py-2"
      >
        <div className="relative flex-1 min-w-[180px]">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)]" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, vehicle, action reference…"
            className="w-full rounded-full bg-white/55 px-9 py-2 text-[12.5px] font-medium text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/30 dark:bg-white/[0.05] dark:focus:bg-white/[0.08]"
          />
          {search && (
            <button
              type="button"
              onClick={() => setSearch("")}
              className="absolute right-2 top-1/2 -translate-y-1/2 cursor-pointer rounded-full p-1 text-[var(--color-ink-500)] hover:bg-white/40 dark:hover:bg-white/[0.08]"
              aria-label="Clear search"
            >
              <X className="h-3 w-3" strokeWidth={2.4} />
            </button>
          )}
        </div>

        <div className="flex items-center gap-1 rounded-full bg-white/45 p-1 dark:bg-white/[0.04]">
          {(["All", "Active", "Inactive"] as const).map((opt) => {
            const active = statusFilter === opt;
            return (
              <button
                key={opt}
                type="button"
                onClick={() => setStatusFilter(opt)}
                className={cn(
                  "cursor-pointer rounded-full px-3 py-1 text-[11px] font-bold uppercase tracking-[0.08em] transition-all",
                  active
                    ? "bg-[var(--color-brand-900)] text-white shadow-[0_2px_6px_-2px_rgba(15,23,42,0.35)] dark:bg-[var(--color-brand-500)]"
                    : "text-[var(--color-ink-600)] hover:text-[var(--color-ink-900)]",
                )}
              >
                {opt}
              </button>
            );
          })}
        </div>

        <button
          type="button"
          onClick={() => refresh()}
          disabled={loading}
          className="inline-flex cursor-pointer items-center gap-1.5 rounded-full bg-white/45 px-3 py-1.5 text-[11.5px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/70 disabled:opacity-50 dark:bg-white/[0.04] dark:hover:bg-white/[0.08]"
        >
          <RefreshCw
            className={cn("h-3.5 w-3.5", loading && "animate-spin")}
            strokeWidth={2.3}
          />
          Refresh
        </button>
      </motion.div>

      {/* Error */}
      {error && (
        <motion.div
          initial={{ opacity: 0, y: -6 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex items-center gap-2 rounded-[var(--radius-sm)] border border-[var(--color-coral)]/30 bg-[var(--color-coral)]/10 px-3 py-2 text-[12.5px] text-[var(--color-coral)]"
        >
          <AlertTriangle className="h-4 w-4" strokeWidth={2.2} />
          {error}
        </motion.div>
      )}

      {/* Table + pagination */}
      <div className="space-y-0">
        <OrderTemplatesTable
          templates={paged}
          loading={loading}
          busyId={busyId}
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={handleSortChange}
          search={debouncedSearch}
          hasFilters={hasFilters}
          onClearFilters={clearFilters}
          onOpenDetail={(t) => setSelected(t)}
          onDispatch={(t) => setDispatching(t)}
          onAction={(action, t) => {
            if (action === "toggle") void handleToggleActive(t);
            else if (action === "delete") setConfirmDelete(t);
          }}
        />
        {!loading && totalCount > 0 && (
          <motion.div
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.15 }}
            className="mt-2 rounded-[var(--radius-xl)] glass"
          >
            <Pagination
              total={totalCount}
              page={page}
              pageSize={pageSize}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
            />
          </motion.div>
        )}
      </div>

      {/* Drawer */}
      <OrderTemplateDrawer
        open={selected !== null}
        template={selected}
        busy={busyId === selected?.id}
        onClose={() => setSelected(null)}
        onDispatch={(t) => {
          setSelected(null);
          setDispatching(t);
        }}
        onToggleActive={(t) => handleToggleActive(t)}
        onDelete={(t) => setConfirmDelete(t)}
      />

      {/* Create-from-template dialog */}
      <CreateFromTemplateDialog
        template={dispatching}
        onClose={() => setDispatching(null)}
        onSuccess={(msg) => {
          void refresh();
          flash("ok", msg);
        }}
      />

      {/* Confirm delete */}
      <ConfirmDeleteDialog
        template={confirmDelete}
        busy={busyId === confirmDelete?.id}
        onCancel={() => setConfirmDelete(null)}
        onConfirm={(t) => handleDelete(t)}
      />

      {/* Toast */}
      <AnimatePresence>
        {toast && (
          <motion.div
            key="toast"
            initial={{ opacity: 0, y: 24, scale: 0.96 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.96, transition: { duration: 0.14 } }}
            transition={{ type: "spring", stiffness: 340, damping: 28 }}
            className={cn(
              "pointer-events-none fixed bottom-6 left-1/2 z-[60] -translate-x-1/2 rounded-full px-5 py-2.5 text-[12.5px] font-semibold shadow-[0_10px_30px_-10px_rgba(15,23,42,0.45)]",
              toast.kind === "ok"
                ? "bg-[var(--color-ink-900)] text-white dark:bg-[var(--color-brand-500)]"
                : "bg-[var(--color-coral)] text-white",
            )}
          >
            {toast.msg}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ── Hero band ──────────────────────────────────────────────────────────
// A premium "value moment" surface that promotes the headline action
// (create an order from a template) and shows the catalog at a glance.

function HeroBand({
  active,
  avgMissions,
  templates,
  onDispatch,
}: {
  active: number;
  avgMissions: number;
  templates: OrderTemplateDto[];
  onDispatch: (t: OrderTemplateDto) => void;
}) {
  // "Featured" template = active with the most missions, tiebreak by recency.
  const featured = useMemo(() => {
    const active = templates.filter((t) => t.isActive);
    if (active.length === 0) return null;
    return [...active].sort((a, b) => {
      const am = a.transportOrder?.missions?.length ?? 0;
      const bm = b.transportOrder?.missions?.length ?? 0;
      if (bm !== am) return bm - am;
      const at = new Date(a.modifiedAt ?? a.createdAt).getTime();
      const bt = new Date(b.modifiedAt ?? b.createdAt).getTime();
      return bt - at;
    })[0];
  }, [templates]);

  return (
    <GlassCard
      variant="strong"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
      className="relative p-6 sm:p-7"
    >
      {/* Decorative aurora */}
      <div className="pointer-events-none absolute inset-0 overflow-hidden rounded-[inherit]">
        <motion.div
          aria-hidden
          className="absolute -top-24 -right-16 h-72 w-72 rounded-full bg-[var(--color-brand-500)]/20 blur-3xl"
          animate={{ scale: [1, 1.15, 1], opacity: [0.5, 0.8, 0.5] }}
          transition={{ duration: 8, repeat: Infinity, ease: "easeInOut" }}
        />
        <motion.div
          aria-hidden
          className="absolute -bottom-20 -left-10 h-64 w-64 rounded-full bg-[var(--color-pastel-peach)] blur-3xl"
          animate={{ scale: [1, 1.1, 1], opacity: [0.35, 0.55, 0.35] }}
          transition={{ duration: 9, repeat: Infinity, ease: "easeInOut", delay: 1.4 }}
        />
      </div>

      <div className="relative flex flex-col gap-5 md:flex-row md:items-center md:justify-between">
        <div className="max-w-xl">
          <div className="inline-flex items-center gap-2 rounded-full bg-white/60 px-3 py-1 text-[10.5px] font-bold uppercase tracking-[0.14em] text-[var(--color-brand-900)] backdrop-blur dark:bg-white/[0.06] dark:text-[var(--color-brand-500)]">
            <StatusPulse tone="brand" />
            Dispatch ready
          </div>
          <h2 className="font-display mt-3 text-[1.8rem] leading-tight font-semibold tracking-tight text-[var(--color-ink-900)] sm:text-[2rem]">
            Turn a template into a live order
          </h2>
          <p className="mt-2 max-w-md text-[13.5px] leading-relaxed text-[var(--color-ink-500)]">
            Pick a template, optionally override priority or vehicle binding, and
            preview the resolved missions with a dry-run before they hit RIOT3.
          </p>

          <div className="mt-4 flex flex-wrap items-center gap-2">
            {featured ? (
              <motion.button
                type="button"
                whileHover={{ y: -1 }}
                whileTap={{ scale: 0.97 }}
                onClick={() => onDispatch(featured)}
                className="group inline-flex cursor-pointer items-center gap-2 rounded-full bg-[var(--color-brand-900)] px-5 py-2.5 text-[12.5px] font-semibold text-white shadow-[0_10px_28px_-10px_rgba(15,23,42,0.55)] transition-shadow hover:shadow-[0_14px_36px_-10px_rgba(15,23,42,0.65)] dark:bg-[var(--color-brand-500)]"
              >
                <Rocket className="h-4 w-4 transition-transform group-hover:-translate-y-0.5 group-hover:translate-x-0.5" strokeWidth={2.3} />
                Create order from “{featured.name}”
              </motion.button>
            ) : (
              <span className="inline-flex items-center gap-2 rounded-full bg-white/55 px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-500)] dark:bg-white/[0.04]">
                <CircleSlash className="h-3.5 w-3.5" strokeWidth={2.3} />
                No active templates — activate one to dispatch
              </span>
            )}
            <Link
              href="/delivery-orders/order-templates/new"
              className="inline-flex items-center gap-1.5 rounded-full bg-white/55 px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.08]"
            >
              <Sparkles className="h-3.5 w-3.5" strokeWidth={2.3} />
              Author a new one
            </Link>
          </div>
        </div>

        {/* Live mini-stats */}
        <div className="grid w-full max-w-xs grid-cols-2 gap-3 md:w-auto">
          <MiniStat
            label="Active now"
            value={
              <span className="font-mono text-[1.55rem] font-semibold text-[var(--color-ink-900)]">
                <NumberTicker value={active} />
              </span>
            }
            hint="templates ready"
          />
          <MiniStat
            label="Avg missions"
            value={
              <span className="font-mono text-[1.55rem] font-semibold text-[var(--color-ink-900)]">
                <NumberTicker value={avgMissions} decimals={1} />
              </span>
            }
            hint="per template"
          />
        </div>
      </div>
    </GlassCard>
  );
}

function MiniStat({
  label,
  value,
  hint,
}: {
  label: string;
  value: React.ReactNode;
  hint: string;
}) {
  return (
    <div className="rounded-[var(--radius-sm)] border border-white/60 bg-white/55 px-3 py-2.5 backdrop-blur dark:border-white/[0.06] dark:bg-white/[0.04]">
      <div className="text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        {label}
      </div>
      <div className="mt-1 leading-none">{value}</div>
      <div className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">{hint}</div>
    </div>
  );
}

// ── KPI tile ───────────────────────────────────────────────────────────

function KpiTile({
  icon,
  label,
  value,
  decimals = 0,
  tone,
  delay,
  loading,
  live,
}: {
  icon: React.ReactNode;
  label: string;
  value: number;
  decimals?: number;
  tone: keyof typeof KPI_TONES;
  delay: number;
  loading?: boolean;
  live?: boolean;
}) {
  return (
    <GlassCard
      variant="default"
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.08 + delay, ease: [0.22, 1, 0.36, 1] }}
      className="p-4 sm:p-5"
    >
      <div className="flex items-start justify-between gap-3">
        <span
          className={cn(
            "grid h-9 w-9 place-items-center rounded-[12px] bg-gradient-to-br shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]",
            KPI_TONES[tone],
          )}
        >
          {icon}
        </span>
        {live && (
          <span className="inline-flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-success)]">
            <StatusPulse tone="success" />
            Live
          </span>
        )}
      </div>
      <div className="mt-4">
        <div className="text-[10.5px] sm:text-[11px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-400)]">
          {label}
        </div>
        <div className="mt-1 flex items-baseline gap-1 font-mono text-[1.55rem] sm:text-[1.65rem] font-semibold leading-none text-[var(--color-ink-900)]">
          {loading ? (
            <motion.span
              initial={{ opacity: 0.3 }}
              animate={{ opacity: [0.3, 0.7, 0.3] }}
              transition={{ duration: 1.4, repeat: Infinity }}
              className="inline-block h-7 w-12 rounded-md bg-[var(--color-ink-100)] dark:bg-white/10"
            />
          ) : (
            <NumberTicker value={value} decimals={decimals} />
          )}
        </div>
      </div>
    </GlassCard>
  );
}

// ── Confirm-delete dialog ───────────────────────────────────────────────

function ConfirmDeleteDialog({
  template,
  busy,
  onCancel,
  onConfirm,
}: {
  template: OrderTemplateDto | null;
  busy: boolean;
  onCancel: () => void;
  onConfirm: (t: OrderTemplateDto) => void;
}) {
  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. Wrapper is pointer-events-none (panel re-enables)
          so a stranded exit can never swallow page clicks. */}
      <OverlayBackdrop
        open={!!template}
        onClick={() => !busy && onCancel()}
        className="z-50 bg-[var(--color-ink-900)]/50 backdrop-blur-md"
      />
      <AnimatePresence>
        {template && (
          <div
            key="delete-template-dialog"
            className="pointer-events-none fixed inset-0 z-[55] flex items-center justify-center p-4"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="pointer-events-auto glass-strong relative w-full max-w-sm overflow-hidden rounded-[var(--radius-xl)] p-6"
            >
              <div className="flex items-start gap-3">
                <span className="grid h-10 w-10 place-items-center rounded-[14px] bg-[var(--color-coral)]/15 text-[var(--color-coral)]">
                  <Trash2 className="h-4 w-4" strokeWidth={2.2} />
                </span>
                <div className="min-w-0">
                  <h3 className="font-display text-[1.15rem] font-semibold text-[var(--color-ink-900)]">
                    Delete template?
                  </h3>
                  <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
                    “{template.name}” will be removed. Any automation that
                    instantiates this template will stop working — make sure
                    nothing depends on it.
                  </p>
                </div>
              </div>
              <div className="mt-5 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={onCancel}
                  disabled={busy}
                  className="cursor-pointer rounded-full px-4 py-2 text-[12.5px] font-semibold text-[var(--color-ink-600)] transition-colors hover:bg-white/40 disabled:opacity-50 dark:hover:bg-white/[0.05]"
                >
                  Cancel
                </button>
                <motion.button
                  type="button"
                  whileTap={{ scale: 0.96 }}
                  onClick={() => onConfirm(template)}
                  disabled={busy}
                  className="cursor-pointer rounded-full bg-[var(--color-coral)] px-5 py-2 text-[12.5px] font-semibold text-white shadow-[0_6px_20px_-8px_rgba(244,79,79,0.5)] disabled:opacity-50"
                >
                  {busy ? "Deleting…" : "Delete"}
                </motion.button>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
