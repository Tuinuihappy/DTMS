"use client";

import { ArrowRight, Box } from "lucide-react";
import { useEffect, useState } from "react";
import { getTripItems, type TripItemDto } from "@/lib/api/trips";
import { cn } from "@/lib/utils";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  TableEmptyState,
  TableSkeleton,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table";

// Phase P5.3 — compact table of every item bound to a trip plus the
// owning order context. Backed by GET /api/v1/dispatch/trips/{id}/items
// (TripItemsProjector). Click an OrderRef cell to open the Order drawer
// stacked on top of the Trip drawer (parent threads onOpenOrder down).
//
// Empty state semantics:
//   • items=[] — render "No items bound yet" (vendor adapter may bind
//     after TripStarted; operator should retry). Different from "trip
//     has zero items by design", which DTMS doesn't currently model.
//   • Error — show the message + a Retry button so flaky network can
//     be resolved without closing/reopening the drawer.
export function TripItemsSection({
  tripId,
  onOpenOrder,
}: {
  tripId: string;
  onOpenOrder?: (orderId: string) => void;
}) {
  const [items, setItems] = useState<TripItemDto[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tripId) {
      setItems(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);

    getTripItems(tripId)
      .then((resp) => {
        if (!cancelled) setItems(resp.items);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [tripId]);

  const retry = () => {
    setError(null);
    setLoading(true);
    getTripItems(tripId)
      .then((resp) => setItems(resp.items))
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  };

  return (
    <section>
      <div className="mb-2 flex items-center justify-between">
        <h3 className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
          Trip items {items ? `(${items.length})` : ""}
        </h3>
      </div>

      {loading && !items && <TableSkeleton label="Loading items…" rows={2} />}

      {error && (
        <div className="rounded-xl bg-[var(--color-coral-soft)] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)]">
          <div>{error}</div>
          <button
            type="button"
            onClick={retry}
            className="mt-2 rounded-md bg-[var(--color-coral)] px-2.5 py-1 text-[10.5px] font-semibold uppercase tracking-[0.06em] text-white"
          >
            Retry
          </button>
        </div>
      )}

      {items && items.length === 0 && !error && (
        <TableEmptyState
          variant="no-data"
          icon={Box}
          title="No items bound to this trip yet"
          body="The vendor adapter may still be binding them — retry in a moment."
        />
      )}

      {items && items.length > 0 && (
        // Outline wrapper (thin border, no glass) — the trip drawer already
        // provides the surrounding card so the inner table reads as a list
        // inside that card rather than a stacked second card.
        <div className="overflow-hidden rounded-xl border border-[var(--color-ink-100)] dark:border-white/[0.06]">
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <DataTableHead>
                <TableTh density="compact">#</TableTh>
                <TableTh density="compact">Lot / description</TableTh>
                <TableTh density="compact">Qty</TableTh>
                <TableTh density="compact">Status</TableTh>
                <TableTh density="compact">Route</TableTh>
                <TableTh density="compact">Order</TableTh>
              </DataTableHead>
              <DataTableBody>
                {items.map((it, i) => (
                  <DataRow key={it.itemPk} delayIndex={i}>
                    <TableTd
                      density="compact"
                      className="font-mono text-[11px] font-semibold text-[var(--color-ink-400)]"
                    >
                      {it.itemSeq.toString().padStart(2, "0")}
                    </TableTd>
                    <TableTd density="compact">
                      <span className="inline-flex items-center gap-1 font-mono text-[11.5px] font-semibold text-[var(--color-ink-900)] dark:text-white">
                        <Box className="h-3 w-3 text-[var(--color-ink-400)]" strokeWidth={2.2} />
                        {it.lotNo}
                      </span>
                      {it.description && (
                        <div
                          className="mt-0.5 max-w-[260px] truncate text-[10.5px] text-[var(--color-ink-500)]"
                          title={it.description}
                        >
                          {it.description}
                        </div>
                      )}
                    </TableTd>
                    <TableTd density="compact">
                      {it.quantity ? (
                        <span className="font-mono text-[11.5px] tabular-nums text-[var(--color-ink-800)] dark:text-[var(--color-ink-300)]">
                          {it.quantity.value}
                          <span className="ml-1 text-[10px] uppercase tracking-[0.04em] text-[var(--color-ink-400)]">
                            {it.quantity.uom}
                          </span>
                        </span>
                      ) : (
                        <span className="text-[var(--color-ink-300)]">—</span>
                      )}
                    </TableTd>
                    <TableTd density="compact">
                      <ItemStatusBadge status={it.itemStatus} />
                    </TableTd>
                    <TableTd density="compact">
                      <span className="inline-flex items-center gap-1 font-mono text-[11px]">
                        <span className="rounded-md bg-[var(--color-pastel-sky)] px-1.5 py-0.5 text-[var(--color-pastel-sky-ink)]">
                          {it.pickupCode ?? "—"}
                        </span>
                        <ArrowRight
                          className="h-3 w-3 text-[var(--color-ink-400)]"
                          strokeWidth={2.4}
                        />
                        <span className="rounded-md bg-[var(--color-pastel-mint)] px-1.5 py-0.5 text-[var(--color-pastel-mint-ink)]">
                          {it.dropCode ?? "—"}
                        </span>
                      </span>
                    </TableTd>
                    <TableTd density="compact">
                      {onOpenOrder ? (
                        <button
                          type="button"
                          onClick={() => onOpenOrder(it.order.id)}
                          className="group inline-flex items-center gap-1.5 rounded-md px-1.5 py-0.5 text-left transition-colors hover:bg-[var(--color-brand-500)]/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-brand-500)]"
                          aria-label={`Open order ${it.order.orderRef}`}
                          title={`Open order ${it.order.orderRef}`}
                        >
                          <span className="font-mono text-[11.5px] font-semibold text-[var(--color-brand-500)] underline-offset-2 group-hover:underline">
                            {it.order.orderRef}
                          </span>
                          <OrderStatusChip status={it.order.status} />
                          {it.order.transportMode && (
                            <TransportModeChip mode={it.order.transportMode} />
                          )}
                        </button>
                      ) : (
                        <span className="inline-flex items-center gap-1.5">
                          <span className="font-mono text-[11.5px] font-semibold text-[var(--color-ink-900)]">
                            {it.order.orderRef}
                          </span>
                          <OrderStatusChip status={it.order.status} />
                          {it.order.transportMode && (
                            <TransportModeChip mode={it.order.transportMode} />
                          )}
                        </span>
                      )}
                    </TableTd>
                  </DataRow>
                ))}
              </DataTableBody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}

// Map ItemStatus strings (from the projector — uses C# enum ToString:
// Pending/Picked/DroppedOff/Delivered/Failed/Returned/Cancelled, plus
// projector-synthesized Delivered/Unbound on terminal trips).
function ItemStatusBadge({ status }: { status: string }) {
  const palette = (() => {
    switch (status) {
      case "Delivered":
        return "bg-[var(--color-success-soft)] text-[var(--color-success)]";
      case "DroppedOff":
      case "Picked":
        return "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]";
      case "Pending":
        return "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]";
      case "Failed":
      case "Cancelled":
      case "Unbound":
        return "bg-[var(--color-coral-soft)] text-[var(--color-coral)]";
      case "Returned":
        return "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]";
      default:
        return "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.05]";
    }
  })();
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-[2px] text-[9.5px] font-bold uppercase tracking-[0.06em]",
        palette,
      )}
    >
      {status}
    </span>
  );
}

function OrderStatusChip({ status }: { status: string }) {
  return (
    <span className="inline-flex items-center rounded bg-[var(--color-ink-100)] px-1.5 py-[1px] text-[9px] font-bold uppercase tracking-[0.06em] text-[var(--color-ink-500)] dark:bg-white/[0.06]">
      {status}
    </span>
  );
}

// Transport mode picked at order-creation time. Pastel-coded so the
// operator can scan the items table and group rows by routing intent
// without reading the label.
function TransportModeChip({ mode }: { mode: string }) {
  const palette = (() => {
    switch (mode) {
      case "Amr":
        return "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]";
      case "Fleet":
        return "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]";
      case "Manual":
        return "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]";
      default:
        return "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06]";
    }
  })();
  return (
    <span
      className={cn(
        "inline-flex items-center rounded px-1.5 py-[1px] text-[9px] font-bold uppercase tracking-[0.06em]",
        palette,
      )}
      title={`Transport mode: ${mode}`}
    >
      {mode}
    </span>
  );
}
