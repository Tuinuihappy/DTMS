"use client";

import { ArrowRight, Box } from "lucide-react";
import { useEffect, useState } from "react";
import { getTripItems, type TripItemDto } from "@/lib/api/trips";
import { cn } from "@/lib/utils";

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

      {loading && !items && (
        <div className="space-y-1">
          {Array.from({ length: 2 }).map((_, i) => (
            <div
              key={i}
              className="h-10 animate-pulse rounded-lg bg-[var(--color-ink-100)] dark:bg-white/[0.05]"
            />
          ))}
        </div>
      )}

      {error && (
        <div className="rounded-xl bg-[#fde0db] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
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
        <div className="rounded-xl border border-dashed border-[var(--color-ink-200)] px-4 py-3 text-[12.5px] text-[var(--color-ink-500)] dark:border-white/[0.08]">
          No items bound to this trip yet. The vendor adapter may still
          be binding them — retry in a moment.
        </div>
      )}

      {items && items.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-[var(--color-ink-100)] dark:border-white/[0.06]">
          <table className="w-full border-collapse text-[12px]">
            <thead className="bg-[var(--color-ink-100)]/40 text-[10px] font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-400)] dark:bg-white/[0.03]">
              <tr>
                <th className="px-2.5 py-1.5 text-left font-semibold">#</th>
                <th className="px-2.5 py-1.5 text-left font-semibold">Lot / description</th>
                <th className="px-2.5 py-1.5 text-left font-semibold">Qty</th>
                <th className="px-2.5 py-1.5 text-left font-semibold">Status</th>
                <th className="px-2.5 py-1.5 text-left font-semibold">Route</th>
                <th className="px-2.5 py-1.5 text-left font-semibold">Order</th>
              </tr>
            </thead>
            <tbody>
              {items.map((it) => (
                <tr
                  key={it.itemPk}
                  className="border-t border-[var(--color-ink-100)] dark:border-white/[0.06]"
                >
                  <td className="px-2.5 py-2 align-middle font-mono text-[11px] font-semibold text-[var(--color-ink-400)]">
                    {it.itemSeq.toString().padStart(2, "0")}
                  </td>
                  <td className="px-2.5 py-2 align-top">
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
                  </td>
                  <td className="px-2.5 py-2 align-middle">
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
                  </td>
                  <td className="px-2.5 py-2 align-middle">
                    <ItemStatusBadge status={it.itemStatus} />
                  </td>
                  <td className="px-2.5 py-2 align-middle">
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
                  </td>
                  <td className="px-2.5 py-2 align-middle">
                    {onOpenOrder ? (
                      <button
                        type="button"
                        onClick={() => onOpenOrder(it.order.id)}
                        className="group inline-flex items-center gap-1.5 rounded-md px-1.5 py-0.5 text-left transition-colors hover:bg-[var(--color-brand-500)]/10"
                        title={`Open order ${it.order.orderRef}`}
                      >
                        <span className="font-mono text-[11.5px] font-semibold text-[var(--color-brand-500)] underline-offset-2 group-hover:underline">
                          {it.order.orderRef}
                        </span>
                        <OrderStatusChip status={it.order.status} />
                      </button>
                    ) : (
                      <span className="inline-flex items-center gap-1.5">
                        <span className="font-mono text-[11.5px] font-semibold text-[var(--color-ink-900)]">
                          {it.order.orderRef}
                        </span>
                        <OrderStatusChip status={it.order.status} />
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
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
        return "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]";
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
