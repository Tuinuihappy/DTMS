"use client";

import { AnimatePresence, motion } from "motion/react";
import {
  AlertTriangle,
  Layers,
  Plus,
  Power,
  RefreshCw,
  Search,
  Sparkles,
  Trash2,
  Workflow,
  X,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { SectionLabel } from "@/components/primitives/section-label";
import { cn } from "@/lib/utils";
import {
  activateActionTemplate,
  deactivateActionTemplate,
  deleteActionTemplate,
  getActionTemplateStats,
  listActionTemplates,
  type ActionCategory,
  type ActionTemplateDto,
  type ActionTemplateStatsDto,
} from "@/lib/api/action-templates";
import { Pagination, type PageSize } from "@/components/delivery-orders/pagination";
import { ActionTemplateDialog } from "./create-dialog";
import { ActionTemplateDrawer } from "./detail-drawer";
import {
  TemplatesTable,
  type SortColumn,
  type SortDir,
} from "./templates-table";

type CategoryFilter = "All" | ActionCategory;

const KPI_TONES = {
  brand:
    "from-[var(--color-pastel-sky)] to-[var(--color-pastel-sky-tail)] text-[var(--color-brand-900)] dark:text-[var(--color-pastel-sky-ink)]",
  amber:
    "from-[var(--color-amber-soft)] to-[var(--color-pastel-amber-tail)] text-[#8a4a07] dark:text-[var(--color-amber)]",
  success:
    "from-[var(--color-success-soft)] to-[var(--color-pastel-mint-tail)] text-[var(--color-success)] dark:text-[var(--color-success)]",
  ink: "from-[var(--color-ink-100)] to-[var(--color-pastel-ink-tail)] text-[var(--color-ink-800)] dark:text-[var(--color-ink-700)]",
} as const;

function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

const EMPTY_STATS: ActionTemplateStatsDto = {
  total: 0,
  active: 0,
  inactive: 0,
  std: 0,
  act: 0,
};

export function ActionTemplatesExperience() {
  const [records, setRecords] = useState<ActionTemplateDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [stats, setStats] = useState<ActionTemplateStatsDto>(EMPTY_STATS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showInactive, setShowInactive] = useState(true);
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>("All");
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search, 250);
  const [sortBy, setSortBy] = useState<SortColumn>("modifiedAt");
  const [sortDir, setSortDir] = useState<SortDir>("desc");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(10);

  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<ActionTemplateDto | null>(null);
  const [duplicating, setDuplicating] = useState<ActionTemplateDto | null>(null);
  const [selected, setSelected] = useState<ActionTemplateDto | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<ActionTemplateDto | null>(null);

  const [toast, setToast] = useState<{ kind: "ok" | "err"; msg: string } | null>(
    null,
  );
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const flash = useCallback((kind: "ok" | "err", msg: string) => {
    setToast({ kind, msg });
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(null), 3200);
  }, []);

  // Reset to page 1 whenever the result set changes — otherwise the user
  // could be stranded on page 7 of a 2-page result after narrowing.
  useEffect(() => {
    setPage(1);
  }, [categoryFilter, showInactive, debouncedSearch, pageSize, sortBy, sortDir]);

  const refresh = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      try {
        const result = await listActionTemplates(
          {
            page,
            size: pageSize,
            includeInactive: showInactive,
            actionCategory: categoryFilter === "All" ? undefined : categoryFilter,
            search: debouncedSearch.trim() || undefined,
            sortBy,
            sortDir,
          },
          signal,
        );
        setRecords(result.records);
        setTotalCount(Number(result.total));
      } catch (e) {
        if ((e as Error).name === "AbortError") return;
        setError((e as Error).message || "Failed to load templates.");
        setRecords([]);
        setTotalCount(0);
      } finally {
        setLoading(false);
      }
    },
    [page, pageSize, showInactive, categoryFilter, debouncedSearch, sortBy, sortDir],
  );

  useEffect(() => {
    const ac = new AbortController();
    void refresh(ac.signal);
    return () => ac.abort();
  }, [refresh]);

  // Stats — KPI strip is a system-wide overview, so we refetch only on
  // mount and after mutations (activate/deactivate/delete) re-trigger
  // refresh below. Independent of list filters by design.
  const refreshStats = useCallback(async (signal?: AbortSignal) => {
    try {
      const s = await getActionTemplateStats(signal);
      setStats(s);
    } catch (e) {
      if ((e as Error).name === "AbortError") return;
      // Non-fatal — KPI strip stays on last-known counters rather than
      // failing the whole page.
    }
  }, []);

  useEffect(() => {
    const ac = new AbortController();
    void refreshStats(ac.signal);
    return () => ac.abort();
  }, [refreshStats]);

  // Guard: if rows shrink (delete on last page) and current page is now
  // beyond the end, step back so the table isn't empty.
  useEffect(() => {
    const lastPage = Math.max(1, Math.ceil(totalCount / pageSize));
    if (page > lastPage) setPage(lastPage);
  }, [totalCount, page, pageSize]);

  const handleSortChange = (col: SortColumn) => {
    if (sortBy === col) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else {
      setSortBy(col);
      setSortDir(col === "actionName" || col === "actionCategory" ? "asc" : "desc");
    }
  };

  const hasFilters =
    categoryFilter !== "All" || debouncedSearch.trim().length > 0 || !showInactive;
  const clearFilters = () => {
    setCategoryFilter("All");
    setSearch("");
    setShowInactive(true);
  };

  async function handleToggleActive(t: ActionTemplateDto) {
    setBusyId(t.id);
    try {
      if (t.isActive) await deactivateActionTemplate(t.id);
      else await activateActionTemplate(t.id);
      await Promise.all([refresh(), refreshStats()]);
      setSelected((cur) => (cur?.id === t.id ? { ...cur, isActive: !t.isActive } : cur));
      flash("ok", t.isActive ? "Template deactivated." : "Template activated.");
    } catch (e) {
      flash("err", (e as Error).message || "Action failed.");
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(t: ActionTemplateDto) {
    setBusyId(t.id);
    try {
      await deleteActionTemplate(t.id);
      await Promise.all([refresh(), refreshStats()]);
      setSelected((cur) => (cur?.id === t.id ? null : cur));
      flash("ok", `Deleted "${t.actionName}".`);
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
        icon={<Workflow className="h-4 w-4" strokeWidth={2.1} />}
        title="Action templates"
        subtitle="Reusable workflow building blocks · compose missions from named, parameterised steps."
        action={
          <motion.button
            type="button"
            whileTap={{ scale: 0.97 }}
            whileHover={{ y: -1 }}
            onClick={() => {
              setEditing(null);
              setDuplicating(null);
              setCreateOpen(true);
            }}
            className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12.5px] font-semibold text-white shadow-[0_6px_22px_-8px_rgba(15,23,42,0.55)] transition-shadow hover:shadow-[0_8px_28px_-8px_rgba(15,23,42,0.65)] dark:bg-[var(--color-brand-500)]"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2.4} />
            New template
          </motion.button>
        }
      />

      {/* KPI strip — matches OrdersKpiStrip pattern */}
      <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
        <KpiTile
          icon={<Layers className="h-4 w-4" strokeWidth={2.2} />}
          label="Total templates"
          value={stats.total}
          tone="brand"
          delay={0}
          loading={loading && records.length === 0}
        />
        <KpiTile
          icon={<Power className="h-4 w-4" strokeWidth={2.2} />}
          label="Active"
          value={stats.active}
          tone="success"
          delay={0.05}
          loading={loading && records.length === 0}
          live={stats.active > 0 && !loading}
        />
        <KpiTile
          icon={<Sparkles className="h-4 w-4" strokeWidth={2.2} />}
          label="STD"
          value={stats.std}
          tone="ink"
          delay={0.1}
          loading={loading && records.length === 0}
        />
        <KpiTile
          icon={<Sparkles className="h-4 w-4" strokeWidth={2.2} />}
          label="ACT"
          value={stats.act}
          tone="amber"
          delay={0.15}
          loading={loading && records.length === 0}
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
            placeholder="Search by name…"
            className="w-full rounded-full bg-white/55 px-9 py-2 text-[12.5px] font-medium text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/30 dark:bg-white/[0.05] dark:focus:bg-white/[0.08]"
          />
          {search && (
            <button
              type="button"
              onClick={() => setSearch("")}
              className="absolute right-2 top-1/2 -translate-y-1/2 rounded-full p-1 text-[var(--color-ink-500)] hover:bg-white/40 dark:hover:bg-white/[0.08]"
              aria-label="Clear search"
            >
              <X className="h-3 w-3" strokeWidth={2.4} />
            </button>
          )}
        </div>

        <div className="flex items-center gap-1 rounded-full bg-white/45 p-1 dark:bg-white/[0.04]">
          {(["All", "Std", "Act"] as const).map((opt) => {
            const active = categoryFilter === opt;
            return (
              <button
                key={opt}
                type="button"
                onClick={() => setCategoryFilter(opt)}
                className={cn(
                  "rounded-full px-3 py-1 text-[11px] font-bold uppercase tracking-[0.08em] transition-all",
                  active
                    ? "bg-[var(--color-brand-900)] text-white shadow-[0_2px_6px_-2px_rgba(15,23,42,0.35)] dark:bg-[var(--color-brand-500)]"
                    : "text-[var(--color-ink-600)] hover:text-[var(--color-ink-900)]",
                )}
              >
                {opt === "All" ? "All" : opt.toUpperCase()}
              </button>
            );
          })}
        </div>

        <label className="inline-flex items-center gap-2 rounded-full bg-white/45 px-3 py-1.5 text-[11.5px] font-semibold text-[var(--color-ink-700)] dark:bg-white/[0.04]">
          <input
            type="checkbox"
            checked={showInactive}
            onChange={(e) => setShowInactive(e.target.checked)}
            className="h-3.5 w-3.5 rounded border-[var(--color-ink-300)] accent-[var(--color-brand-500)]"
          />
          Include inactive
        </label>

        <button
          type="button"
          onClick={() => refresh()}
          disabled={loading}
          className="inline-flex items-center gap-1.5 rounded-full bg-white/45 px-3 py-1.5 text-[11.5px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/70 disabled:opacity-50 dark:bg-white/[0.04] dark:hover:bg-white/[0.08]"
        >
          <RefreshCw
            className={cn("h-3.5 w-3.5", loading && "animate-spin")}
            strokeWidth={2.3}
          />
          Refresh
        </button>
      </motion.div>

      {/* Error banner */}
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

      {/* Table + Pagination — wrapped in space-y-0 so the bar sits flush
          against the table, matching the orders-list layout. */}
      <div className="space-y-0">
        <TemplatesTable
          templates={records}
          loading={loading}
          busyId={busyId}
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={handleSortChange}
          search={debouncedSearch}
          hasFilters={hasFilters}
          onClearFilters={clearFilters}
          onCreate={() => {
            setEditing(null);
            setDuplicating(null);
            setCreateOpen(true);
          }}
          onOpenDetail={(t) => setSelected(t)}
          onAction={(action, t) => {
            if (action === "edit") {
              setDuplicating(null);
              setEditing(t);
              setCreateOpen(true);
            } else if (action === "duplicate") {
              setEditing(null);
              setDuplicating(t);
              setCreateOpen(true);
            } else if (action === "toggle") {
              void handleToggleActive(t);
            } else if (action === "delete") {
              setConfirmDelete(t);
            }
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

      {/* Dialogs */}
      <ActionTemplateDialog
        open={createOpen}
        editing={editing}
        duplicating={duplicating}
        onClose={() => {
          setCreateOpen(false);
          setDuplicating(null);
          setEditing(null);
        }}
        onSaved={() => {
          void refresh();
          void refreshStats();
          flash(
            "ok",
            editing
              ? "Template updated."
              : duplicating
                ? "Template duplicated."
                : "Template created.",
          );
        }}
      />

      <ActionTemplateDrawer
        open={selected !== null}
        template={selected}
        busy={busyId === selected?.id}
        onClose={() => setSelected(null)}
        onEdit={(t) => {
          setDuplicating(null);
          setEditing(t);
          setSelected(null);
          setCreateOpen(true);
        }}
        onDuplicate={(t) => {
          setEditing(null);
          setDuplicating(t);
          setSelected(null);
          setCreateOpen(true);
        }}
        onToggleActive={(t) => handleToggleActive(t)}
        onDelete={(t) => setConfirmDelete(t)}
      />

      <ConfirmDeleteDialog
        template={confirmDelete}
        busy={busyId === confirmDelete?.id}
        onCancel={() => setConfirmDelete(null)}
        onConfirm={(t) => handleDelete(t)}
      />

      <AnimatePresence>
        {toast && (
          <motion.div
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

// ── KPI tile (mirrors OrdersKpiStrip) ──────────────────────────────────

function KpiTile({
  icon,
  label,
  value,
  tone,
  delay,
  loading,
  live,
}: {
  icon: React.ReactNode;
  label: string;
  value: number;
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
            <NumberTicker value={value} decimals={0} />
          )}
        </div>
      </div>
    </GlassCard>
  );
}

// ── Confirm-delete dialog ──────────────────────────────────────────────

function ConfirmDeleteDialog({
  template,
  busy,
  onCancel,
  onConfirm,
}: {
  template: ActionTemplateDto | null;
  busy: boolean;
  onCancel: () => void;
  onConfirm: (t: ActionTemplateDto) => void;
}) {
  return (
    <AnimatePresence>
      {template && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !busy && onCancel()}
            className="fixed inset-0 z-50 bg-[var(--color-ink-900)]/50 backdrop-blur-md"
          />
          <div className="fixed inset-0 z-[55] flex items-center justify-center p-4">
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="glass-strong relative w-full max-w-sm overflow-hidden rounded-[var(--radius-xl)] p-6"
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
                    "{template.actionName}" will be removed. Order templates that
                    reference it will break — make sure nothing depends on it.
                  </p>
                </div>
              </div>
              <div className="mt-5 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={onCancel}
                  disabled={busy}
                  className="rounded-full px-4 py-2 text-[12.5px] font-semibold text-[var(--color-ink-600)] transition-colors hover:bg-white/40 disabled:opacity-50 dark:hover:bg-white/[0.05]"
                >
                  Cancel
                </button>
                <motion.button
                  type="button"
                  whileTap={{ scale: 0.96 }}
                  onClick={() => onConfirm(template)}
                  disabled={busy}
                  className="rounded-full bg-[var(--color-coral)] px-5 py-2 text-[12.5px] font-semibold text-white shadow-[0_6px_20px_-8px_rgba(244,79,79,0.5)] disabled:opacity-50"
                >
                  {busy ? "Deleting…" : "Delete"}
                </motion.button>
              </div>
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
  );
}
