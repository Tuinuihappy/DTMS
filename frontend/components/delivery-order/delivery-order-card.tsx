"use client";

import { PackageCheck } from "lucide-react";

import { cn } from "@/lib/utils";
import {
  formatEnumLabel,
  type DeliveryOrderListDto,
} from "@/types/delivery-order";

import { StatusBadge } from "./status-badge";

interface DeliveryOrderCardProps {
  order: DeliveryOrderListDto;
  selected: boolean;
  onSelect: (id: string) => void;
}

// Compact card for the Delivery Orders grid. Tap-target is the whole
// card — clicking opens the detail Sheet. Top-right shows the lifecycle
// status pill so it's scannable across many cards at once.
export function DeliveryOrderCard({
  order,
  selected,
  onSelect,
}: DeliveryOrderCardProps) {
  return (
    <button
      type="button"
      onClick={() => onSelect(order.id)}
      className={cn(
        "press-feedback group liquid-glass relative overflow-hidden rounded-2xl p-4 text-left transition-all duration-200",
        "hover:shadow-[0_8px_28px_-4px_rgba(0,0,0,0.10)]",
        selected && "ring-2 ring-primary/35"
      )}
    >
      <div className="relative z-[2] flex items-start justify-between gap-2">
        <div
          className="liquid-puck flex h-9 w-9 items-center justify-center rounded-2xl"
          style={
            {
              ["--tint" as string]:
                "color-mix(in oklch, oklch(0.78 0.13 180) 80%, white)",
            } as React.CSSProperties
          }
        >
          <PackageCheck
            className="relative z-[2] h-3.5 w-3.5 text-white"
            strokeWidth={2.25}
          />
        </div>
        <StatusBadge status={order.orderStatus} />
      </div>

      <div className="relative z-[2] mt-3 space-y-1">
        <h3 className="truncate font-mono text-[13px] font-semibold tracking-tight">
          {order.orderRef}
        </h3>
        <p className="text-[12px] text-muted-foreground">
          {order.totalItems} item{order.totalItems === 1 ? "" : "s"} ·{" "}
          {order.totalWeightKg > 0
            ? `${order.totalWeightKg.toFixed(1)} kg`
            : "no weight"}{" "}
          · {formatEnumLabel(order.priority)}
        </p>
        <ServiceWindowLine
          earliestUtc={order.serviceWindow?.earliestUtc}
          latestUtc={order.serviceWindow?.latestUtc}
        />
      </div>

      <div className="relative z-[2] mt-3">
        {/* The whole card is already a real <button>; this "View" chip
            is a visual cue only — using a span avoids the nested-button
            HTML violation while keeping the same look. */}
        <span className="flex h-7 w-full items-center justify-center rounded-full bg-black/[0.04] text-[12px] font-medium text-foreground group-hover:bg-black/[0.07] dark:bg-white/[0.06] dark:group-hover:bg-white/[0.10]">
          View
        </span>
      </div>
    </button>
  );
}

function ServiceWindowLine({
  earliestUtc,
  latestUtc,
}: {
  earliestUtc?: string | null;
  latestUtc?: string | null;
}) {
  if (!earliestUtc && !latestUtc) {
    return (
      <p className="text-[11px] text-muted-foreground/70">No window set</p>
    );
  }
  const fmt = (s: string) => {
    const d = new Date(s);
    return d.toLocaleString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  };
  return (
    <p className="truncate text-[11px] text-muted-foreground/80">
      {earliestUtc ? fmt(earliestUtc) : "—"} →{" "}
      {latestUtc ? fmt(latestUtc) : "—"}
    </p>
  );
}
