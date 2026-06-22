"use client";

import {
  ArrowRight,
  Check,
  ChevronRight,
  MoreHorizontal,
  Pencil,
  Send,
  Trash2,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import type {
  DeliveryOrderListDto,
  OrderStatus,
} from "@/lib/api/delivery-orders";
import { PriorityBadge, StatusBadge, TransportModeBadge } from "./badges";
import { Highlight } from "./highlight";
import { DateTime } from "@/components/primitives/date-time";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  DataTableShell,
  MobileCardRow,
  SortableTh,
  TableEmptyState,
  TableSkeleton,
  TableTd,
  TableTh,
  resolveEmptyStateVariant,
} from "@/components/primitives/data-table";

type RowAction = "edit" | "submit" | "confirm" | "delete";

export type SortColumn =
  | "createdDate"
  | "orderRef"
  | "priority"
  | "status"
  | "totalWeightKg";
export type SortDir = "asc" | "desc";

const EDITABLE: OrderStatus[] = ["Draft"];
const SUBMITTABLE: OrderStatus[] = ["Draft"];
const CONFIRMABLE: OrderStatus[] = ["Submitted", "Validated"];
const DELETABLE: OrderStatus[] = ["Draft", "Submitted", "Validated"];

function allowedActions(o: DeliveryOrderListDto): RowAction[] {
  const out: RowAction[] = [];
  if (EDITABLE.includes(o.orderStatus)) out.push("edit");
  if (SUBMITTABLE.includes(o.orderStatus)) out.push("submit");
  if (CONFIRMABLE.includes(o.orderStatus)) out.push("confirm");
  if (DELETABLE.includes(o.orderStatus)) out.push("delete");
  return out;
}

function shortRef(ref: string): string {
  return ref.length > 18 ? ref.slice(0, 15) + "…" : ref;
}

type Props = {
  orders: DeliveryOrderListDto[];
  loading: boolean;
  selected: Set<string>;
  onSelectionChange: (next: Set<string>) => void;
  onOpenDetail: (id: string) => void;
  onAction: (action: RowAction, order: DeliveryOrderListDto) => void;
  sortBy: SortColumn;
  sortDir: SortDir;
  onSortChange: (col: SortColumn) => void;
  search: string;
  // Hints the empty state — true when the user has any filter set so we
  // can offer "Clear filters" instead of "Create your first order".
  hasFilters: boolean;
  onClearFilters: () => void;
};

export function OrdersTable({
  orders,
  loading,
  selected,
  onSelectionChange,
  onOpenDetail,
  onAction,
  sortBy,
  sortDir,
  onSortChange,
  search,
  hasFilters,
  onClearFilters,
}: Props) {
  const allSelected = orders.length > 0 && orders.every((o) => selected.has(o.id));
  const someSelected = orders.some((o) => selected.has(o.id));
  const headerRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (headerRef.current) {
      headerRef.current.indeterminate = !allSelected && someSelected;
    }
  }, [allSelected, someSelected]);

  const toggleAll = () => {
    if (allSelected) {
      onSelectionChange(new Set());
    } else {
      onSelectionChange(new Set(orders.map((o) => o.id)));
    }
  };

  const toggleOne = (id: string) => {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onSelectionChange(next);
  };

  if (loading && orders.length === 0)
    return <TableSkeleton label="Loading orders…" />;
  if (!loading && orders.length === 0)
    return (
      <OrdersEmptyState
        search={search}
        hasFilters={hasFilters}
        onClearFilters={onClearFilters}
      />
    );

  return (
    <>
      {/* Desktop table — primitives handle padding/header/sort/keyboard/focus.
          Bulk-select checkbox column stops click propagation on the input
          itself so toggling a row doesn't open the detail drawer. */}
      <DataTableShell className="hidden md:block">
        <DataTableHead>
          <TableTh className="w-10" aria-label="Select all">
            <input
              ref={headerRef}
              type="checkbox"
              checked={allSelected}
              onChange={toggleAll}
              aria-label={
                allSelected ? "Deselect all orders on this page" : "Select all orders on this page"
              }
              className="h-3.5 w-3.5 rounded border-[var(--color-ink-300)] accent-[var(--color-brand-500)]"
            />
          </TableTh>
          <SortableTh col="orderRef" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Order
          </SortableTh>
          <TableTh>Route</TableTh>
          <SortableTh col="status" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Status
          </SortableTh>
          <SortableTh col="priority" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Priority
          </SortableTh>
          <TableTh>Transport</TableTh>
          <TableTh align="right">Items</TableTh>
          <SortableTh
            col="totalWeightKg"
            sortBy={sortBy}
            sortDir={sortDir}
            onSort={onSortChange}
            align="right"
          >
            Weight
          </SortableTh>
          <TableTh>Window</TableTh>
          <SortableTh col="createdDate" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Created
          </SortableTh>
          <TableTh className="w-12" aria-label="Actions">
            <span className="sr-only">Actions</span>
          </TableTh>
        </DataTableHead>
        <DataTableBody>
          <AnimatePresence initial={false}>
            {orders.map((o, i) => (
              <DataRow
                key={o.id}
                delayIndex={i}
                selected={selected.has(o.id)}
                onClick={() => onOpenDetail(o.id)}
              >
                <TableTd>
                  <input
                    type="checkbox"
                    checked={selected.has(o.id)}
                    onClick={(e) => e.stopPropagation()}
                    onChange={() => toggleOne(o.id)}
                    aria-label={`Select order ${o.orderRef}`}
                    className="h-3.5 w-3.5 rounded border-[var(--color-ink-300)] accent-[var(--color-brand-500)]"
                  />
                </TableTd>
                <TableTd>
                  <div className="font-mono text-[13px] font-semibold text-[var(--color-ink-900)]">
                    <Highlight text={shortRef(o.orderRef)} query={search} />
                  </div>
                  {o.requestedBy && (
                    <div
                      className="text-[11px] text-[var(--color-ink-400)] truncate max-w-[140px]"
                      title={`by ${o.requestedBy}`}
                    >
                      by <Highlight text={o.requestedBy} query={search} />
                    </div>
                  )}
                </TableTd>
                <TableTd>
                  <RouteCell from={firstFrom(o)} to={firstTo(o)} />
                </TableTd>
                <TableTd>
                  <StatusBadge status={o.orderStatus} />
                </TableTd>
                <TableTd>
                  <PriorityBadge priority={o.priority} />
                </TableTd>
                <TableTd>
                  <TransportModeBadge mode={o.requestedTransportMode} />
                </TableTd>
                <TableTd
                  align="right"
                  className="font-mono text-[12.5px] tabular-nums text-[var(--color-ink-800)]"
                >
                  {o.totalItems}
                </TableTd>
                <TableTd
                  align="right"
                  className="font-mono text-[12.5px] tabular-nums text-[var(--color-ink-800)]"
                >
                  {o.totalWeightKg > 0 ? (
                    <>
                      {o.totalWeightKg.toLocaleString("en-US", { maximumFractionDigits: 1 })}
                      <span className="ml-0.5 text-[var(--color-ink-400)] text-[10.5px]">kg</span>
                    </>
                  ) : (
                    <span className="text-[var(--color-ink-400)]">—</span>
                  )}
                </TableTd>
                <TableTd className="font-mono text-[12px] text-[var(--color-ink-600)]">
                  <DateTime
                    value={o.serviceWindow?.latestUtc ?? o.serviceWindow?.earliestUtc}
                    variant="time"
                  />
                </TableTd>
                <TableTd className="text-[11.5px] text-[var(--color-ink-500)] whitespace-nowrap">
                  <DateTime value={o.createdDate} variant="relative" />
                </TableTd>
                <TableTd align="right">
                  <RowMenu
                    order={o}
                    onAction={onAction}
                    onOpenDetail={() => onOpenDetail(o.id)}
                  />
                </TableTd>
              </DataRow>
            ))}
          </AnimatePresence>
        </DataTableBody>
      </DataTableShell>

      {/* Mobile cards — separate glass card so the table primitives stay
          table-shaped. Same domain content, different layout. */}
      <div className="md:hidden overflow-hidden rounded-[var(--radius-xl)] glass divide-y divide-[var(--color-ink-100)]/60 dark:divide-white/[0.04]">
        <AnimatePresence initial={false}>
          {orders.map((o, i) => (
            <MobileCardRow
              key={o.id}
              delayIndex={i}
              selected={selected.has(o.id)}
              ariaLabel={`Order ${o.orderRef} — ${o.orderStatus}`}
              onClick={() => onOpenDetail(o.id)}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <input
                      type="checkbox"
                      checked={selected.has(o.id)}
                      onClick={(e) => e.stopPropagation()}
                      onChange={() => toggleOne(o.id)}
                      aria-label={`Select order ${o.orderRef}`}
                      className="h-3.5 w-3.5 shrink-0 rounded border-[var(--color-ink-300)] accent-[var(--color-brand-500)]"
                    />
                    <div className="font-mono text-[13px] font-semibold text-[var(--color-ink-900)] truncate">
                      <Highlight text={shortRef(o.orderRef)} query={search} />
                    </div>
                    <PriorityBadge priority={o.priority} />
                  </div>
                  <div className="mt-2">
                    <RouteCell from={firstFrom(o)} to={firstTo(o)} />
                  </div>
                </div>
                <ChevronRight className="h-4 w-4 shrink-0 text-[var(--color-ink-400)] mt-1" />
              </div>
              <div className="mt-3 flex flex-wrap items-center gap-3 text-[11.5px] text-[var(--color-ink-500)]">
                <StatusBadge status={o.orderStatus} />
                <TransportModeBadge mode={o.requestedTransportMode} />
                <span className="font-mono">
                  {o.totalItems} items · {o.totalWeightKg.toFixed(0)}kg
                </span>
                <DateTime
                  value={o.createdDate}
                  variant="relative"
                  className="ml-auto"
                />
              </div>
            </MobileCardRow>
          ))}
        </AnimatePresence>
      </div>
    </>
  );
}

function RouteCell({ from, to }: { from: string; to: string }) {
  return (
    <div className="flex items-center gap-2 max-w-[200px]">
      <span className="font-mono text-[12px] text-[var(--color-ink-700)] truncate">
        {from || "—"}
      </span>
      <ArrowRight
        className="h-3 w-3 shrink-0 text-[var(--color-brand-500)]"
        strokeWidth={2.4}
      />
      <span className="font-mono text-[12px] text-[var(--color-ink-700)] truncate">
        {to || "—"}
      </span>
    </div>
  );
}

// The list DTO doesn't include item routes (only the detail DTO does).
// Show "{totalItems} stops" so the column still carries a signal at-a-
// glance; the drawer reveals the actual pick→drop graph.
function firstFrom(_o: DeliveryOrderListDto): string {
  return "Pickup";
}
function firstTo(_o: DeliveryOrderListDto): string {
  return "Drop";
}

function RowMenu({
  order,
  onAction,
  onOpenDetail,
}: {
  order: DeliveryOrderListDto;
  onAction: (a: RowAction, o: DeliveryOrderListDto) => void;
  onOpenDetail: () => void;
}) {
  const [open, setOpen] = useState(false);
  const actions = allowedActions(order);
  useEffect(() => {
    if (!open) return;
    const handle = () => setOpen(false);
    const t = setTimeout(() => window.addEventListener("click", handle), 0);
    return () => {
      clearTimeout(t);
      window.removeEventListener("click", handle);
    };
  }, [open]);

  return (
    <div className="relative inline-block" onClick={(e) => e.stopPropagation()}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="rounded-md p-1.5 text-[var(--color-ink-500)] opacity-0 transition-all group-hover:opacity-100 focus-visible:opacity-100 hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
        aria-label={`Actions for ${order.orderRef}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <MoreHorizontal className="h-4 w-4" strokeWidth={2.2} />
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, scale: 0.94, y: -4 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: -4, transition: { duration: 0.12 } }}
            transition={{ type: "spring", stiffness: 460, damping: 30 }}
            role="menu"
            aria-label={`Actions for ${order.orderRef}`}
            className="absolute right-0 z-20 mt-1 w-44 origin-top-right overflow-hidden rounded-xl glass-strong shadow-[0_20px_50px_-15px_rgba(15,23,42,0.35)]"
          >
            <MenuItem onClick={() => { setOpen(false); onOpenDetail(); }}>
              <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.2} />
              View details
            </MenuItem>
            {actions.includes("edit") && (
              <MenuItem
                onClick={() => { setOpen(false); onAction("edit", order); }}
              >
                <Pencil className="h-3.5 w-3.5" strokeWidth={2.2} />
                Edit draft
              </MenuItem>
            )}
            {actions.includes("submit") && (
              <MenuItem
                tone="brand"
                onClick={() => { setOpen(false); onAction("submit", order); }}
              >
                <Send className="h-3.5 w-3.5" strokeWidth={2.2} />
                Submit
              </MenuItem>
            )}
            {actions.includes("confirm") && (
              <MenuItem
                tone="success"
                onClick={() => { setOpen(false); onAction("confirm", order); }}
              >
                <Check className="h-3.5 w-3.5" strokeWidth={2.2} />
                Confirm
              </MenuItem>
            )}
            {actions.includes("delete") && (
              <MenuItem
                tone="coral"
                onClick={() => { setOpen(false); onAction("delete", order); }}
              >
                <Trash2 className="h-3.5 w-3.5" strokeWidth={2.2} />
                Cancel order
              </MenuItem>
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function MenuItem({
  onClick,
  tone = "default",
  children,
}: {
  onClick: () => void;
  tone?: "default" | "brand" | "success" | "coral";
  children: React.ReactNode;
}) {
  const tones = {
    default: "text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] dark:hover:bg-white/10",
    brand: "text-[var(--color-brand-500)] hover:bg-[var(--color-pastel-sky)]",
    success: "text-[var(--color-success)] hover:bg-[var(--color-success-soft)]",
    coral: "text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]",
  };
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex w-full items-center gap-2 px-3 py-2 text-left text-[12.5px] font-semibold transition-colors",
        tones[tone],
      )}
    >
      {children}
    </button>
  );
}

// ── Empty state ────────────────────────────────────────────────────────

function OrdersEmptyState({
  search,
  hasFilters,
  onClearFilters,
}: {
  search: string;
  hasFilters: boolean;
  onClearFilters: () => void;
}) {
  const variant = resolveEmptyStateVariant(search, hasFilters);
  const trimmed = search.trim();

  if (variant === "no-search-match") {
    return (
      <TableEmptyState
        variant={variant}
        icon={Send}
        title="No orders match your search"
        body={
          <>
            Nothing found for{" "}
            <span className="font-mono font-semibold text-[var(--color-ink-700)]">
              “{trimmed}”
            </span>
            . Check the spelling or widen your status filter.
          </>
        }
        action={
          hasFilters
            ? { label: "Clear filters", onClick: onClearFilters }
            : undefined
        }
      />
    );
  }

  if (variant === "no-filter-match") {
    return (
      <TableEmptyState
        variant={variant}
        icon={Send}
        title="No orders in this view"
        body="The current filters returned no rows. Clear them to see everything."
        action={{ label: "Clear filters", onClick: onClearFilters }}
      />
    );
  }

  return (
    <TableEmptyState
      variant={variant}
      icon={Send}
      title="No orders yet"
      body="Use the “New order” button to create your first delivery."
    />
  );
}
