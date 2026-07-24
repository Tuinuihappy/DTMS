"use client";

import {
  ArrowRight,
  Loader2,
  Package,
  Search,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { useCallback, useEffect, useState } from "react";
import { PermissionGuard } from "@/components/auth/permission-guard";
import { Pagination, type PageSize } from "@/components/delivery-orders/pagination";
import {
  DataTableBody,
  DataTableHead,
  DataTableShell,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table/table-shell";
import { TableEmptyState } from "@/components/primitives/data-table/table-empty-state";
import { GlassCard } from "@/components/primitives/glass-card";
import {
  formatItemStatus,
  getItem,
  ITEM_STATUS_OPTIONS,
  searchItems,
  type ItemDetail,
  type ItemSearchResult,
  type ItemStatusWire,
} from "@/lib/api/items";
import { Permissions } from "@/lib/auth/permissions";
import { cn } from "@/lib/utils";

const STATUS_TONE: Record<ItemStatusWire, string> = {
  PENDING: "bg-[var(--color-pastel-sky)] text-[var(--color-brand-900)]",
  PICKED: "bg-[var(--color-pastel-lavender)] text-[var(--color-brand-900)]",
  DROPPED_OFF: "bg-[var(--color-pastel-peach)] text-[var(--color-brand-900)]",
  DELIVERED: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
  FAILED: "bg-[var(--color-coral-soft)] text-[var(--color-coral)]",
  RETURNED: "bg-[var(--color-pastel-peach)] text-[var(--color-brand-900)]",
  CANCELLED: "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06]",
};

function StatusBadge({ status }: { status: ItemStatusWire }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-[10.5px] font-semibold uppercase tracking-[0.06em]",
        STATUS_TONE[status] ?? STATUS_TONE.CANCELLED,
      )}
    >
      {formatItemStatus(status)}
    </span>
  );
}

type Filters = {
  itemId: string;
  status: string;
  pickupCode: string;
  dropCode: string;
};

const EMPTY_FILTERS: Filters = { itemId: "", status: "", pickupCode: "", dropCode: "" };

export function ItemsExperience() {
  return (
    <PermissionGuard requires={Permissions.DeliveryOrder.ItemRead}>
      <ItemsInner />
    </PermissionGuard>
  );
}

function ItemsInner() {
  // Draft (input) vs applied (query) filters — applied on submit so typing
  // doesn't spam the backend.
  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(25);

  const [rows, setRows] = useState<ItemSearchResult[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const hasFilters =
    !!applied.itemId || !!applied.status || !!applied.pickupCode || !!applied.dropCode;

  const refresh = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      searchItems(
        {
          itemId: applied.itemId || undefined,
          status: applied.status || undefined,
          pickupCode: applied.pickupCode || undefined,
          dropCode: applied.dropCode || undefined,
          page,
          pageSize,
        },
        signal,
      )
        .then((res) => {
          setRows(res.data);
          setTotal(res.totalCount);
        })
        .catch((e: Error) => {
          if (e.name !== "AbortError") setError(e.message || "Failed to load items");
        })
        .finally(() => setLoading(false));
    },
    [applied, page, pageSize],
  );

  useEffect(() => {
    const ac = new AbortController();
    refresh(ac.signal);
    return () => ac.abort();
  }, [refresh]);

  const submit = () => {
    setApplied(draft);
    setPage(1);
  };

  const clear = () => {
    setDraft(EMPTY_FILTERS);
    setApplied(EMPTY_FILTERS);
    setPage(1);
  };

  return (
    <div className="space-y-5">
      <header className="flex items-center gap-3">
        <span className="grid h-10 w-10 place-items-center rounded-[14px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[var(--color-pastel-lavender)] text-[var(--color-brand-900)]">
          <Package className="h-5 w-5" strokeWidth={2.1} />
        </span>
        <div>
          <h1 className="font-display text-[1.35rem] font-semibold text-[var(--color-ink-900)]">
            Items
          </h1>
          <p className="text-[12.5px] text-[var(--color-ink-500)]">
            Search delivery-order items across every order.
          </p>
        </div>
      </header>

      {/* Filter bar */}
      <GlassCard className="px-4 py-3">
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Item ID">
            <input
              value={draft.itemId}
              onChange={(e) => setDraft((d) => ({ ...d, itemId: e.target.value }))}
              onKeyDown={(e) => e.key === "Enter" && submit()}
              placeholder="LINE-001"
              className={inputClass}
            />
          </Field>
          <Field label="Status">
            <select
              value={draft.status}
              onChange={(e) => setDraft((d) => ({ ...d, status: e.target.value }))}
              className={inputClass}
            >
              <option value="">Any</option>
              {ITEM_STATUS_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Pickup code">
            <input
              value={draft.pickupCode}
              onChange={(e) => setDraft((d) => ({ ...d, pickupCode: e.target.value }))}
              onKeyDown={(e) => e.key === "Enter" && submit()}
              placeholder="WH-A-DOCK-01"
              className={inputClass}
            />
          </Field>
          <Field label="Drop code">
            <input
              value={draft.dropCode}
              onChange={(e) => setDraft((d) => ({ ...d, dropCode: e.target.value }))}
              onKeyDown={(e) => e.key === "Enter" && submit()}
              placeholder="LAB-3-BENCH-12"
              className={inputClass}
            />
          </Field>
          <div className="flex items-center gap-2">
            <button type="button" onClick={submit} className={primaryBtn}>
              <Search className="h-3.5 w-3.5" strokeWidth={2.4} />
              Search
            </button>
            {hasFilters && (
              <button type="button" onClick={clear} className={ghostBtn}>
                Clear
              </button>
            )}
          </div>
        </div>
      </GlassCard>

      {/* Results */}
      {error ? (
        <GlassCard className="px-6 py-10 text-center">
          <p className="text-[13px] font-medium text-[var(--color-coral)]">{error}</p>
          <button type="button" onClick={() => refresh()} className={cn(ghostBtn, "mt-4")}>
            Retry
          </button>
        </GlassCard>
      ) : loading && rows.length === 0 ? (
        <GlassCard className="grid place-items-center px-6 py-16">
          <Loader2 className="h-6 w-6 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.2} />
        </GlassCard>
      ) : rows.length === 0 ? (
        <TableEmptyState
          variant={hasFilters ? "no-filter-match" : "no-data"}
          title={hasFilters ? "No items match" : "No items yet"}
          body={
            hasFilters
              ? "Try widening or clearing the filters."
              : "Items appear here once orders are created."
          }
          icon={Package}
          action={hasFilters ? { label: "Clear filters", onClick: clear } : undefined}
        />
      ) : (
        <>
          <DataTableShell className={cn(loading && "opacity-60 transition-opacity")}>
            <DataTableHead>
              <TableTh>Item</TableTh>
              <TableTh>Order</TableTh>
              <TableTh>Route</TableTh>
              <TableTh align="right">Qty</TableTh>
              <TableTh align="right">Weight</TableTh>
              <TableTh>Status</TableTh>
            </DataTableHead>
            <DataTableBody>
              {rows.map((it) => (
                <tr
                  key={it.id}
                  onClick={() => setSelectedId(it.id)}
                  className="cursor-pointer border-t border-white/40 transition-colors hover:bg-white/40 dark:border-white/[0.05] dark:hover:bg-white/[0.04]"
                >
                  <TableTd>
                    <div className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)]">
                      {it.itemId}
                    </div>
                    {it.description && (
                      <div className="mt-0.5 max-w-[220px] truncate text-[11.5px] text-[var(--color-ink-500)]">
                        {it.description}
                      </div>
                    )}
                  </TableTd>
                  <TableTd>
                    <div className="text-[12.5px] font-semibold text-[var(--color-ink-800)]">
                      {it.order.orderRef}
                    </div>
                    <div className="mt-0.5 text-[10.5px] uppercase tracking-[0.06em] text-[var(--color-ink-400)]">
                      {formatItemStatus(it.order.orderStatus)}
                    </div>
                  </TableTd>
                  <TableTd>
                    <div className="flex items-center gap-1.5 text-[12px] text-[var(--color-ink-700)]">
                      <span className="font-mono">{it.pickupLocationCode}</span>
                      <ArrowRight className="h-3 w-3 text-[var(--color-ink-400)]" strokeWidth={2.4} />
                      <span className="font-mono">{it.dropLocationCode}</span>
                    </div>
                  </TableTd>
                  <TableTd align="right">
                    <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-800)]">
                      {it.quantity.value} {it.quantity.uom}
                    </span>
                  </TableTd>
                  <TableTd align="right">
                    <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-700)]">
                      {it.weightKg != null ? `${it.weightKg} kg` : "—"}
                    </span>
                  </TableTd>
                  <TableTd>
                    <StatusBadge status={it.status} />
                  </TableTd>
                </tr>
              ))}
            </DataTableBody>
          </DataTableShell>

          <GlassCard>
            <Pagination
              total={total}
              page={page}
              pageSize={pageSize}
              onPageChange={setPage}
              onPageSizeChange={(s) => {
                setPageSize(s);
                setPage(1);
              }}
            />
          </GlassCard>
        </>
      )}

      <ItemDetailDrawer itemId={selectedId} onClose={() => setSelectedId(null)} />
    </div>
  );
}

// ── Detail drawer ────────────────────────────────────────────────────────
function ItemDetailDrawer({
  itemId,
  onClose,
}: {
  itemId: string | null;
  onClose: () => void;
}) {
  const [detail, setDetail] = useState<ItemDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!itemId) return;
    setDetail(null);
    setError(null);
    setLoading(true);
    getItem(itemId)
      .then(setDetail)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [itemId]);

  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. */}
      <OverlayBackdrop
        open={!!itemId}
        onClick={onClose}
        className="z-40 bg-[var(--color-ink-900)]/45 backdrop-blur-sm"
      />
      <AnimatePresence>
        {itemId && (
          <motion.aside
            key="item-drawer-panel"
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 320, damping: 34 }}
            className="fixed right-0 top-0 z-50 h-full w-full max-w-md overflow-y-auto glass-strong"
          >
            <header className="flex items-center justify-between border-b border-white/40 px-5 py-4 dark:border-white/[0.06]">
              <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                Item detail
              </div>
              <button
                type="button"
                onClick={onClose}
                className="rounded-full p-1.5 text-[var(--color-ink-500)] transition-colors hover:bg-white/40 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                aria-label="Close"
              >
                <X className="h-4 w-4" strokeWidth={2.4} />
              </button>
            </header>

            <div className="px-5 py-5">
              {loading ? (
                <div className="grid place-items-center py-16">
                  <Loader2 className="h-6 w-6 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.2} />
                </div>
              ) : error ? (
                <p className="text-[13px] font-medium text-[var(--color-coral)]">{error}</p>
              ) : detail ? (
                <div className="space-y-4">
                  <div className="flex items-center justify-between">
                    <div className="font-mono text-[15px] font-semibold text-[var(--color-ink-900)]">
                      {detail.itemId}
                    </div>
                    <StatusBadge status={detail.status} />
                  </div>
                  {detail.description && (
                    <p className="text-[13px] text-[var(--color-ink-600)]">{detail.description}</p>
                  )}
                  <DetailGrid
                    rows={[
                      ["Pickup", detail.pickupLocationCode],
                      ["Drop", detail.dropLocationCode],
                      ["Quantity", `${detail.quantity.value} ${detail.quantity.uom}`],
                      ["Weight", detail.weightKg != null ? `${detail.weightKg} kg` : "—"],
                      [
                        "Load unit",
                        detail.loadUnitProfileCode ?? "—",
                      ],
                      [
                        "Dimensions",
                        detail.dimensions
                          ? `${detail.dimensions.lengthMm}×${detail.dimensions.widthMm}×${detail.dimensions.heightMm} mm`
                          : "—",
                      ],
                      [
                        "Hazmat",
                        detail.hazmat
                          ? `Class ${detail.hazmat.classCode}${detail.hazmat.packingGroup ? ` · PG ${detail.hazmat.packingGroup}` : ""}`
                          : "—",
                      ],
                      [
                        "Temperature",
                        detail.temperature
                          ? `${detail.temperature.minC ?? "?"}–${detail.temperature.maxC ?? "?"} °C`
                          : "—",
                      ],
                    ]}
                  />
                  {detail.handlingInstructions.length > 0 && (
                    <div>
                      <div className="mb-1.5 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
                        Handling
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {detail.handlingInstructions.map((h) => (
                          <span
                            key={h}
                            className="rounded-full bg-[var(--color-pastel-mint)] px-2.5 py-0.5 text-[10.5px] font-semibold text-[var(--color-brand-900)]"
                          >
                            {formatItemStatus(h)}
                          </span>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              ) : null}
            </div>
          </motion.aside>
        )}
      </AnimatePresence>
    </>
  );
}

function DetailGrid({ rows }: { rows: [string, string][] }) {
  return (
    <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-2 text-[12.5px]">
      {rows.map(([k, v]) => (
        <div key={k} className="contents">
          <dt className="pt-0.5 text-[10.5px] font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-400)]">
            {k}
          </dt>
          <dd className="text-[var(--color-ink-800)]">{v}</dd>
        </div>
      ))}
    </dl>
  );
}

// ── shared styles ────────────────────────────────────────────────────────
function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        {label}
      </span>
      {children}
    </label>
  );
}

const inputClass =
  "h-9 rounded-md border border-white/70 bg-white/60 px-2.5 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]";

const primaryBtn =
  "inline-flex h-9 items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 text-[12px] font-semibold text-white transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]";

const ghostBtn =
  "inline-flex h-9 items-center rounded-full bg-white/50 px-4 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]";
