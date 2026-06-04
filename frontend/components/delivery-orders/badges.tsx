"use client";

import { Bot, Hand, Truck } from "lucide-react";
import { cn } from "@/lib/utils";
import type { OrderStatus, Priority, TransportMode } from "@/lib/api/delivery-orders";

// Status → pastel category mapping. Buckets the 15 backend states into the
// 4 pastel families so the table reads as a glance-able dashboard rather
// than a rainbow lookup table. "Live" statuses get a pulse dot.
type StatusVisual = {
  label: string;
  tone: "ink" | "sky" | "lavender" | "mint" | "peach" | "success" | "amber" | "coral";
  pulse?: boolean;
};

const STATUS_VISUAL: Record<OrderStatus, StatusVisual> = {
  Draft: { label: "Draft", tone: "ink" },
  Submitted: { label: "Submitted", tone: "sky" },
  Validated: { label: "Validated", tone: "sky" },
  Confirmed: { label: "Confirmed", tone: "lavender" },
  Planning: { label: "Planning", tone: "lavender", pulse: true },
  Planned: { label: "Planned", tone: "mint" },
  Dispatched: { label: "Dispatched", tone: "peach", pulse: true },
  InProgress: { label: "In progress", tone: "peach", pulse: true },
  Completed: { label: "Completed", tone: "success" },
  PartiallyCompleted: { label: "Partial", tone: "amber" },
  Held: { label: "Held", tone: "amber" },
  Failed: { label: "Failed", tone: "coral" },
  Amended: { label: "Amended", tone: "ink" },
  Cancelled: { label: "Cancelled", tone: "ink" },
  Rejected: { label: "Rejected", tone: "coral" },
};

const TONE_BG: Record<StatusVisual["tone"], string> = {
  ink: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
  sky: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
  lavender:
    "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]",
  mint: "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
  peach: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
  success: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
  amber: "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
  coral: "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]",
};

const TONE_DOT: Record<StatusVisual["tone"], string> = {
  ink: "bg-[var(--color-ink-400)]",
  sky: "bg-[var(--color-brand-500)]",
  lavender: "bg-[var(--color-pastel-lavender-ink)]",
  mint: "bg-[var(--color-success)]",
  peach: "bg-[var(--color-amber)]",
  success: "bg-[var(--color-success)]",
  amber: "bg-[var(--color-amber)]",
  coral: "bg-[var(--color-coral)]",
};

export function StatusBadge({ status, size = "sm" }: { status: OrderStatus; size?: "sm" | "md" }) {
  const v = STATUS_VISUAL[status] ?? { label: status, tone: "ink" as const };
  const sizes = size === "md" ? "px-2.5 py-1 text-[11.5px]" : "px-2 py-[3px] text-[10.5px]";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full font-semibold uppercase tracking-[0.08em] whitespace-nowrap",
        sizes,
        TONE_BG[v.tone],
      )}
    >
      <span className="relative inline-flex h-1.5 w-1.5">
        <span className={cn("absolute inset-0 rounded-full", TONE_DOT[v.tone])} />
        {v.pulse && (
          <span
            className={cn(
              "absolute inset-0 rounded-full animate-ping opacity-60",
              TONE_DOT[v.tone],
            )}
          />
        )}
      </span>
      {v.label}
    </span>
  );
}

const PRIORITY_VISUAL: Record<
  Priority,
  { label: string; classes: string; bars: number }
> = {
  Low: {
    label: "Low",
    classes: "text-[var(--color-ink-500)]",
    bars: 1,
  },
  Normal: {
    label: "Normal",
    classes: "text-[var(--color-ink-700)]",
    bars: 2,
  },
  High: {
    label: "High",
    classes: "text-[var(--color-amber)]",
    bars: 3,
  },
  Critical: {
    label: "Critical",
    classes: "text-[var(--color-coral)]",
    bars: 4,
  },
};

export function PriorityBadge({ priority }: { priority: Priority }) {
  const v = PRIORITY_VISUAL[priority];
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 font-mono text-[11px] font-semibold uppercase",
        v.classes,
      )}
      title={`${v.label} priority`}
    >
      <span className="inline-flex items-end gap-[2px]">
        {[1, 2, 3, 4].map((n) => (
          <span
            key={n}
            className={cn(
              "w-[3px] rounded-full transition-all",
              n <= v.bars ? "bg-current" : "bg-current opacity-15",
            )}
            style={{ height: `${4 + n * 2}px` }}
          />
        ))}
      </span>
      {v.label}
    </span>
  );
}

export function TransportModeBadge({ mode }: { mode: TransportMode | null }) {
  if (!mode)
    return (
      <span className="font-mono text-[11px] text-[var(--color-ink-400)]">—</span>
    );
  const config = {
    Amr: {
      label: "AMR",
      icon: Bot,
      cls: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
    },
    Manual: {
      label: "Manual",
      icon: Hand,
      cls: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
    },
    Fleet: {
      label: "Fleet",
      icon: Truck,
      cls: "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
    },
  }[mode];
  const Icon = config.icon;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md px-1.5 py-[3px] text-[10.5px] font-semibold uppercase tracking-[0.06em]",
        config.cls,
      )}
    >
      <Icon className="h-3 w-3" strokeWidth={2.2} />
      {config.label}
    </span>
  );
}

// Glassy "category chip" used in the inline filter bar.
export function FilterChip({
  active,
  onClick,
  children,
  count,
  tone = "ink",
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
  count?: number;
  tone?: StatusVisual["tone"];
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "group inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-[12px] font-semibold transition-all duration-200",
        "border border-transparent",
        active
          ? cn(TONE_BG[tone], "shadow-[0_4px_14px_-6px_rgba(15,23,42,0.18)] scale-[1.02]")
          : "bg-white/40 text-[var(--color-ink-600)] hover:bg-white/70 dark:bg-white/[0.04] dark:text-[var(--color-ink-600)] dark:hover:bg-white/[0.08]",
      )}
    >
      <span>{children}</span>
      {typeof count === "number" && (
        <span
          className={cn(
            "rounded-full px-1.5 py-[1px] font-mono text-[10px] tabular-nums",
            active
              ? "bg-white/40 dark:bg-black/20"
              : "bg-[var(--color-ink-100)] text-[var(--color-ink-600)] dark:bg-white/10",
          )}
        >
          {count}
        </span>
      )}
    </button>
  );
}
