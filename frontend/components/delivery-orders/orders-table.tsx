"use client";

import {
  ChevronRight,
  Copy,
  MoreHorizontal,
  Pencil,
  Send,
  Trash2,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { useFlashOnChange } from "@/lib/hooks/use-flash-on-change";
import type {
  DeliveryOrderListDto,
  OrderStatus,
} from "@/lib/api/delivery-orders";
import { PriorityBadge, StatusBadge, TransportModeBadge } from "./badges";
import { Highlight } from "./highlight";
import { DateTime } from "@/components/primitives/date-time";
import { IdCell } from "@/components/primitives/id-cell";
import { RowMenuPortal } from "@/components/primitives/row-menu-portal";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  DataTableShell,
  MobileCardRow,
  RowGroup,
  SortableTh,
  TableEmptyState,
  TableSkeleton,
  TableTd,
  TableTh,
  resolveEmptyStateVariant,
} from "@/components/primitives/data-table";

type RowAction = "edit" | "revise" | "reorder" | "submit" | "delete";

export type SortColumn =
  | "createdDate"
  | "orderRef"
  | "priority"
  | "status"
  | "totalWeightKg";
export type SortDir = "asc" | "desc";

const EDITABLE: OrderStatus[] = ["Draft"];
const SUBMITTABLE: OrderStatus[] = ["Draft"];
// Phase P5 — DELETABLE dropped Submitted/Validated because those states
// no longer occur durably: submit auto-confirms atomically.
const DELETABLE: OrderStatus[] = ["Draft"];

function allowedActions(o: DeliveryOrderListDto): RowAction[] {
  const out: RowAction[] = [];
  if (EDITABLE.includes(o.orderStatus)) out.push("edit");
  // Edit & resend — fix a Failed order's data and re-plan it.
  if (o.orderStatus === "Failed") out.push("revise");
  if (SUBMITTABLE.includes(o.orderStatus)) out.push("submit");
  if (DELETABLE.includes(o.orderStatus)) out.push("delete");
  // Reorder is always available — the source order is unchanged, and a
  // fresh draft can be made from any past order regardless of status.
  out.push("reorder");
  return out;
}

function shortRef(ref: string): string {
  return ref.length > 18 ? ref.slice(0, 15) + "…" : ref;
}

type Props = {
  orders: DeliveryOrderListDto[];
  // page|sort|filter key of `orders` — drives the query-change fade and
  // keeps realtime row flashes off navigation.
  signature: string;
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
  // Load failure. Replaces the table only while there is nothing to show —
  // without it a failed first load fell through to "No orders yet".
  error?: string | null;
  onRetry?: () => void;
};

// What counts as "this row changed" for the realtime flash.
const orderId = (o: DeliveryOrderListDto) => o.id;
const orderVersion = (o: DeliveryOrderListDto) =>
  `${o.orderStatus}|${o.updatedDate ?? ""}`;

export function OrdersTable({
  orders,
  signature,
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
  error,
  onRetry,
}: Props) {
  const allSelected = orders.length > 0 && orders.every((o) => selected.has(o.id));
  const someSelected = orders.some((o) => selected.has(o.id));
  const headerRef = useRef<HTMLInputElement>(null);
  const flashing = useFlashOnChange(orders, orderId, orderVersion, signature);
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
  if (error && orders.length === 0)
    return (
      <div className="rounded-2xl border border-[var(--color-coral)]/20 bg-[var(--color-coral-soft)]/40 p-6 text-center" role="alert">
        <p className="text-[13px] font-semibold text-[var(--color-coral)]">Couldn&apos;t load orders · {error}</p>
        {onRetry && (
          <button
            type="button"
            onClick={onRetry}
            className="mt-3 rounded-lg border border-[var(--color-coral)]/30 px-3 py-1.5 text-[12px] font-medium text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]/40"
          >
            Retry
          </button>
        )}
      </div>
    );
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
          <TableTh>Order ID</TableTh>
          <SortableTh col="orderRef" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Order
          </SortableTh>
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
        <DataTableBody signature={signature}>
            {orders.map((o, i) => (
              <DataRow
                key={o.id}
                delayIndex={i}
                flash={flashing.has(o.id)}
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
                  <IdCell id={o.id} label="order id" />
                </TableTd>
                <TableTd>
                  <IdCell
                    id={o.orderRef}
                    label="order ref"
                    className="text-[13px] font-semibold text-[var(--color-ink-900)]"
                  >
                    <Highlight text={shortRef(o.orderRef)} query={search} />
                  </IdCell>
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
        </DataTableBody>
      </DataTableShell>

      {/* Mobile cards — separate glass card so the table primitives stay
          table-shaped. Same domain content, different layout. */}
      <RowGroup
        as="div"
        signature={signature}
        className="md:hidden overflow-hidden rounded-[var(--radius-xl)] glass divide-y divide-[var(--color-ink-100)]/60 dark:divide-white/[0.04]"
      >
          {orders.map((o, i) => (
            <MobileCardRow
              key={o.id}
              delayIndex={i}
              flash={flashing.has(o.id)}
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
      </RowGroup>
    </>
  );
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
  const triggerRef = useRef<HTMLButtonElement>(null);

  return (
    <div className="inline-block" onClick={(e) => e.stopPropagation()}>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="rounded-md p-1.5 text-[var(--color-ink-500)] opacity-0 transition-[background-color,color,opacity] group-hover:opacity-100 focus-visible:opacity-100 hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
        aria-label={`Actions for ${order.orderRef}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <MoreHorizontal className="h-4 w-4" strokeWidth={2.2} />
      </button>
      <RowMenuPortal
        open={open}
        onClose={() => setOpen(false)}
        triggerRef={triggerRef}
        width={176}
        ariaLabel={`Actions for ${order.orderRef}`}
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
        {actions.includes("revise") && (
          <MenuItem
            tone="brand"
            onClick={() => { setOpen(false); onAction("revise", order); }}
          >
            <Pencil className="h-3.5 w-3.5" strokeWidth={2.2} />
            Edit &amp; resend
          </MenuItem>
        )}
        {actions.includes("reorder") && (
          <MenuItem
            onClick={() => { setOpen(false); onAction("reorder", order); }}
          >
            <Copy className="h-3.5 w-3.5" strokeWidth={2.2} />
            Reorder
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
        {actions.includes("delete") && (
          <MenuItem
            tone="coral"
            onClick={() => { setOpen(false); onAction("delete", order); }}
          >
            <Trash2 className="h-3.5 w-3.5" strokeWidth={2.2} />
            Cancel order
          </MenuItem>
        )}
      </RowMenuPortal>
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
