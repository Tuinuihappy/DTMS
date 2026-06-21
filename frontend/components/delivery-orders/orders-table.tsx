"use client";

import {
  ArrowRight,
  ArrowUpDown,
  Check,
  ChevronDown,
  ChevronRight,
  ChevronUp,
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

  if (loading && orders.length === 0) return <TableSkeleton />;
  if (!loading && orders.length === 0)
    return (
      <EmptyState
        search={search}
        hasFilters={hasFilters}
        onClearFilters={onClearFilters}
      />
    );

  return (
    <div className="overflow-hidden rounded-[var(--radius-xl)] glass">
      {/* Desktop table */}
      <div className="hidden md:block overflow-x-auto">
        <table className="w-full text-left">
          <thead>
            <tr className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
              <th className="px-5 py-3.5 w-10">
                <input
                  ref={headerRef}
                  type="checkbox"
                  checked={allSelected}
                  onChange={toggleAll}
                  className="h-3.5 w-3.5 rounded border-[var(--color-ink-300)] accent-[var(--color-brand-500)]"
                />
              </th>
              <SortableTh col="orderRef" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Order
              </SortableTh>
              <th className="px-3 py-3.5">Route</th>
              <SortableTh col="status" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Status
              </SortableTh>
              <SortableTh col="priority" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Priority
              </SortableTh>
              <th className="px-3 py-3.5">Transport</th>
              <th className="px-3 py-3.5 text-right">Items</th>
              <SortableTh
                col="totalWeightKg"
                sortBy={sortBy}
                sortDir={sortDir}
                onClick={onSortChange}
                align="right"
              >
                Weight
              </SortableTh>
              <th className="px-3 py-3.5">Window</th>
              <SortableTh
                col="createdDate"
                sortBy={sortBy}
                sortDir={sortDir}
                onClick={onSortChange}
              >
                Created
              </SortableTh>
              <th className="px-5 py-3.5 w-12" />
            </tr>
          </thead>
          <tbody>
            <AnimatePresence initial={false}>
              {orders.map((o, i) => (
                <motion.tr
                  key={o.id}
                  layout
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, transition: { duration: 0.18 } }}
                  transition={{
                    duration: 0.4,
                    delay: Math.min(i * 0.025, 0.4),
                    ease: [0.22, 1, 0.36, 1],
                  }}
                  onClick={() => onOpenDetail(o.id)}
                  className={cn(
                    "group cursor-pointer border-t border-[var(--color-ink-100)]/60 dark:border-white/[0.04]",
                    "transition-colors duration-150 hover:bg-white/40 dark:hover:bg-white/[0.03]",
                    selected.has(o.id) && "bg-[var(--color-pastel-sky)]/40 dark:bg-[var(--color-pastel-sky)]/60",
                  )}
                >
                  <td className="px-5 py-3.5" onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      checked={selected.has(o.id)}
                      onChange={() => toggleOne(o.id)}
                      className="h-3.5 w-3.5 rounded border-[var(--color-ink-300)] accent-[var(--color-brand-500)]"
                    />
                  </td>
                  <td className="px-3 py-3.5">
                    <div className="font-mono text-[13px] font-semibold text-[var(--color-ink-900)]">
                      <Highlight text={shortRef(o.orderRef)} query={search} />
                    </div>
                    {o.requestedBy && (
                      <div className="text-[11px] text-[var(--color-ink-400)] truncate max-w-[140px]">
                        by <Highlight text={o.requestedBy} query={search} />
                      </div>
                    )}
                  </td>
                  <td className="px-3 py-3.5">
                    <RouteCell from={firstFrom(o)} to={firstTo(o)} />
                  </td>
                  <td className="px-3 py-3.5">
                    <StatusBadge status={o.orderStatus} />
                  </td>
                  <td className="px-3 py-3.5">
                    <PriorityBadge priority={o.priority} />
                  </td>
                  <td className="px-3 py-3.5">
                    <TransportModeBadge mode={o.requestedTransportMode} />
                  </td>
                  <td className="px-3 py-3.5 text-right font-mono text-[12.5px] tabular-nums text-[var(--color-ink-800)]">
                    {o.totalItems}
                  </td>
                  <td className="px-3 py-3.5 text-right font-mono text-[12.5px] tabular-nums text-[var(--color-ink-800)]">
                    {o.totalWeightKg > 0 ? (
                      <>
                        {o.totalWeightKg.toLocaleString("en-US", { maximumFractionDigits: 1 })}
                        <span className="ml-0.5 text-[var(--color-ink-400)] text-[10.5px]">kg</span>
                      </>
                    ) : (
                      <span className="text-[var(--color-ink-400)]">—</span>
                    )}
                  </td>
                  <td className="px-3 py-3.5 font-mono text-[12px] text-[var(--color-ink-600)]">
                    <DateTime
                      value={o.serviceWindow?.latestUtc ?? o.serviceWindow?.earliestUtc}
                      variant="time"
                    />
                  </td>
                  <td className="px-3 py-3.5 text-[11.5px] text-[var(--color-ink-500)] whitespace-nowrap">
                    <DateTime value={o.createdDate} variant="relative" />
                  </td>
                  <td className="px-5 py-3.5 text-right">
                    <RowMenu
                      order={o}
                      onAction={onAction}
                      onOpenDetail={() => onOpenDetail(o.id)}
                    />
                  </td>
                </motion.tr>
              ))}
            </AnimatePresence>
          </tbody>
        </table>
      </div>

      {/* Mobile cards */}
      <div className="md:hidden divide-y divide-[var(--color-ink-100)]/60 dark:divide-white/[0.04]">
        <AnimatePresence initial={false}>
          {orders.map((o, i) => (
            <motion.div
              key={o.id}
              layout
              initial={{ opacity: 0, y: 6 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.35, delay: Math.min(i * 0.03, 0.4) }}
              className={cn(
                "px-4 py-4 cursor-pointer transition-colors",
                "active:bg-white/40 dark:active:bg-white/[0.03]",
                selected.has(o.id) && "bg-[var(--color-pastel-sky)]/40 dark:bg-[var(--color-pastel-sky)]/60",
              )}
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
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </div>
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
    coral: "text-[var(--color-coral)] hover:bg-[#fde0db] dark:hover:bg-[#3a1a17]",
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

function SortableTh({
  col,
  sortBy,
  sortDir,
  onClick,
  align = "left",
  children,
}: {
  col: SortColumn;
  sortBy: SortColumn;
  sortDir: SortDir;
  onClick: (col: SortColumn) => void;
  align?: "left" | "right";
  children: React.ReactNode;
}) {
  const active = sortBy === col;
  // Inactive columns show a faint ArrowUpDown so the affordance is
  // visible but doesn't compete with the active sort indicator.
  const Icon = !active ? ArrowUpDown : sortDir === "asc" ? ChevronUp : ChevronDown;
  return (
    <th
      className={cn("px-3 py-3.5", align === "right" && "text-right")}
      aria-sort={active ? (sortDir === "asc" ? "ascending" : "descending") : "none"}
    >
      <button
        type="button"
        onClick={() => onClick(col)}
        aria-label={`Sort by ${typeof children === "string" ? children : col}${
          active ? `, currently ${sortDir === "asc" ? "ascending" : "descending"}` : ""
        }`}
        className={cn(
          "group inline-flex items-center gap-1 transition-colors duration-150",
          align === "right" && "flex-row-reverse",
          active
            ? "text-[var(--color-ink-900)]"
            : "text-[var(--color-ink-400)] hover:text-[var(--color-ink-700)]",
        )}
      >
        <span>{children}</span>
        <Icon
          className={cn(
            "h-3 w-3 transition-opacity",
            active ? "opacity-100" : "opacity-50 group-hover:opacity-80",
          )}
          strokeWidth={2.4}
        />
      </button>
    </th>
  );
}

function TableSkeleton() {
  return (
    <div className="rounded-[var(--radius-xl)] glass p-2">
      <div className="px-3 py-3 flex gap-3 text-[10.5px] uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        <span>Loading orders…</span>
      </div>
      <div className="space-y-2 px-2 pb-2">
        {Array.from({ length: 6 }).map((_, i) => (
          <motion.div
            key={i}
            initial={{ opacity: 0 }}
            animate={{ opacity: [0.4, 0.7, 0.4] }}
            transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.06 }}
            className="h-12 rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.04]"
          />
        ))}
      </div>
    </div>
  );
}

function EmptyState({
  search,
  hasFilters,
  onClearFilters,
}: {
  search: string;
  hasFilters: boolean;
  onClearFilters: () => void;
}) {
  const trimmed = search.trim();
  // Three distinct states: searched but no match → mention the query;
  // filtered but no match → mention filters + offer to clear; truly
  // empty system → onboarding nudge.
  const variant = trimmed
    ? "no-search-match"
    : hasFilters
      ? "no-filter-match"
      : "no-data";

  const copy = {
    "no-search-match": {
      title: "No orders match your search",
      body: (
        <>
          Nothing found for{" "}
          <span className="font-mono font-semibold text-[var(--color-ink-700)]">
            “{trimmed}”
          </span>
          . Check the spelling or widen your status filter.
        </>
      ),
    },
    "no-filter-match": {
      title: "No orders in this view",
      body: <>The current filters returned no rows. Clear them to see everything.</>,
    },
    "no-data": {
      title: "No orders yet",
      body: <>Use the “New order” button to create your first delivery.</>,
    },
  }[variant];

  return (
    <div className="rounded-[var(--radius-xl)] glass px-6 py-16 text-center">
      <motion.div
        animate={{ y: [0, -4, 0] }}
        transition={{ duration: 3, repeat: Infinity, ease: "easeInOut" }}
        className="mx-auto grid h-16 w-16 place-items-center rounded-[20px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[var(--color-pastel-lavender)] text-[var(--color-brand-900)]"
      >
        <Send className="h-6 w-6" strokeWidth={2} />
      </motion.div>
      <h3 className="font-display mt-5 text-lg font-semibold text-[var(--color-ink-900)]">
        {copy.title}
      </h3>
      <p className="mt-1 text-[13px] text-[var(--color-ink-500)] mx-auto max-w-sm">
        {copy.body}
      </p>
      {hasFilters && variant !== "no-data" && (
        <motion.button
          type="button"
          onClick={onClearFilters}
          whileHover={{ y: -1 }}
          whileTap={{ scale: 0.97 }}
          className="mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12px] font-semibold text-white transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]"
        >
          Clear filters
        </motion.button>
      )}
    </div>
  );
}
