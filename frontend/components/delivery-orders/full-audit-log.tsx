"use client";

import {
  Bot,
  Edit3,
  History,
  Repeat2,
  ScrollText,
  Truck,
  User,
} from "lucide-react";
import { motion } from "motion/react";
import { useEffect, useMemo, useState } from "react";
import { getFullOrderAudit, type FullAuditEntryDto, type FullOrderAuditDto } from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";

type SourceFilter = "All" | "Order" | "TripExecution" | "TripRetry" | "Amendment";

/**
 * Consolidated audit view replacing the thin OrderAuditEvent-only
 * timeline. Pulls from order events + amendments + per-trip execution
 * events + retry triggers (backed by /audit-full). Filter chips let
 * operators narrow to just the source they care about; clicking a
 * trip-tagged entry asks the parent to open that Trip's drawer.
 */
export function FullAuditLog({
  orderId,
  onOpenTrip,
}: {
  orderId: string;
  onOpenTrip?: (tripId: string) => void;
}) {
  const [data, setData] = useState<FullOrderAuditDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<SourceFilter>("All");

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    getFullOrderAudit(orderId)
      .then((d) => {
        if (!cancelled) setData(d);
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
  }, [orderId]);

  // Count per source, used in filter chips so operators see what's
  // available before clicking. Recomputed only when data changes.
  const counts = useMemo(() => {
    const c = { All: 0, Order: 0, TripExecution: 0, TripRetry: 0, Amendment: 0 } as
      Record<SourceFilter, number>;
    if (!data) return c;
    for (const e of data.entries) {
      c.All++;
      c[e.source as SourceFilter] = (c[e.source as SourceFilter] ?? 0) + 1;
    }
    return c;
  }, [data]);

  const filtered = useMemo(() => {
    if (!data) return [];
    if (filter === "All") return data.entries;
    return data.entries.filter((e) => e.source === filter);
  }, [data, filter]);

  if (error) {
    return (
      <div className="rounded-xl bg-[#fde0db] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
        Could not load audit log: {error}
      </div>
    );
  }

  if (loading && !data) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 3 }).map((_, i) => (
          <div
            key={i}
            className="h-12 animate-pulse rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.05]"
          />
        ))}
      </div>
    );
  }

  if (!data || data.entries.length === 0) return null;

  return (
    <div>
      {/* Source filter chips */}
      <div className="mb-3 flex flex-wrap gap-1.5">
        {(["All", "Order", "TripExecution", "TripRetry", "Amendment"] as SourceFilter[]).map((s) => {
          const count = counts[s];
          if (count === 0 && s !== "All") return null;
          const active = filter === s;
          return (
            <button
              key={s}
              type="button"
              onClick={() => setFilter(s)}
              className={cn(
                "inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[10.5px] font-semibold uppercase tracking-[0.06em] transition-all",
                active
                  ? "bg-[var(--color-brand-500)] text-white"
                  : "bg-[var(--color-ink-100)] text-[var(--color-ink-700)] hover:bg-[var(--color-ink-200)] dark:bg-white/[0.06] dark:text-[var(--color-ink-500)]",
              )}
            >
              {labelFor(s)}
              <span
                className={cn(
                  "rounded-full px-1 font-mono text-[9px] tabular-nums",
                  active ? "bg-white/30" : "bg-[var(--color-ink-200)] dark:bg-white/10",
                )}
              >
                {count}
              </span>
            </button>
          );
        })}
      </div>

      {/* Timeline */}
      <ol className="relative space-y-2.5 pl-5">
        <span
          aria-hidden
          className="absolute left-[7px] top-2 bottom-2 w-px bg-[var(--color-ink-100)] dark:bg-white/10"
        />
        {filtered.map((entry, idx) => (
          <AuditEntryRow
            key={entry.id}
            entry={entry}
            onOpenTrip={onOpenTrip}
            delayIndex={idx}
          />
        ))}
      </ol>
    </div>
  );
}

function AuditEntryRow({
  entry,
  onOpenTrip,
  delayIndex,
}: {
  entry: FullAuditEntryDto;
  onOpenTrip?: (id: string) => void;
  delayIndex: number;
}) {
  const visual = sourceVisual(entry.source);
  const Icon = visual.icon;
  const friendlyTime = relativeTime(entry.occurredAt);

  return (
    <motion.li
      initial={{ opacity: 0, x: -6 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.28, delay: Math.min(delayIndex * 0.03, 0.4) }}
      className="relative"
    >
      <span
        aria-hidden
        className={cn(
          "absolute -left-[18px] top-1 grid h-[14px] w-[14px] place-items-center rounded-full",
          visual.dotBg,
        )}
      >
        <Icon className="h-2.5 w-2.5 text-white" strokeWidth={2.8} />
      </span>

      <div
        className={cn(
          "ml-1 rounded-xl px-3 py-2",
          "bg-[var(--color-ink-100)]/30 dark:bg-white/[0.03]",
        )}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <span
                className={cn(
                  "rounded-md px-1.5 py-[2px] text-[10px] font-bold uppercase tracking-[0.06em]",
                  visual.chip,
                )}
              >
                {entry.eventType}
              </span>
              {entry.attemptNumber !== null && entry.attemptNumber > 1 && (
                <span className="font-mono text-[10px] font-semibold text-[var(--color-pastel-lavender-ink)]">
                  attempt {entry.attemptNumber}
                </span>
              )}
              {entry.relatedTripId && onOpenTrip && (
                <button
                  type="button"
                  onClick={() => onOpenTrip(entry.relatedTripId!)}
                  className="inline-flex items-center gap-1 rounded-md bg-[var(--color-pastel-sky)] px-1.5 py-[2px] text-[10px] font-semibold text-[var(--color-pastel-sky-ink)] transition-opacity hover:opacity-80"
                >
                  <Truck className="h-2.5 w-2.5" strokeWidth={2.4} />
                  open trip
                </button>
              )}
            </div>
            {entry.details && (
              <p className="mt-1 text-[12px] leading-snug text-[var(--color-ink-700)]">
                {entry.details}
              </p>
            )}
            {entry.actorId && (
              <p className="mt-1 inline-flex items-center gap-1 text-[10.5px] text-[var(--color-ink-400)]">
                <User className="h-2.5 w-2.5" strokeWidth={2.4} />
                <span className="font-mono">{entry.actorId}</span>
              </p>
            )}
          </div>
          <time
            className="font-mono text-[10.5px] text-[var(--color-ink-400)] whitespace-nowrap"
            title={new Date(entry.occurredAt).toLocaleString()}
          >
            {friendlyTime}
          </time>
        </div>
      </div>
    </motion.li>
  );
}

function labelFor(s: SourceFilter): string {
  return {
    All: "All",
    Order: "Order",
    TripExecution: "Trip",
    TripRetry: "Retry",
    Amendment: "Amend",
  }[s];
}

function sourceVisual(source: string): {
  icon: React.ComponentType<{ className?: string; strokeWidth?: number }>;
  dotBg: string;
  chip: string;
} {
  switch (source) {
    case "Order":
      return {
        icon: ScrollText,
        dotBg: "bg-[var(--color-brand-500)]",
        chip: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
      };
    case "TripExecution":
      return {
        icon: Bot,
        dotBg: "bg-[var(--color-amber)]",
        chip: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
      };
    case "TripRetry":
      return {
        icon: Repeat2,
        dotBg: "bg-[var(--color-pastel-lavender-ink)]",
        chip: "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]",
      };
    case "Amendment":
      return {
        icon: Edit3,
        dotBg: "bg-[var(--color-success)]",
        chip: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
      };
    default:
      return {
        icon: History,
        dotBg: "bg-[var(--color-ink-400)]",
        chip: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
      };
  }
}

function relativeTime(iso: string): string {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const min = Math.round(seconds / 60);
  if (min < 60) return `${min}m ago`;
  const hours = Math.round(min / 60);
  if (hours < 48) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  return `${days}d ago`;
}
