"use client";

import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

// Phase P0.F1 — Shared chip that surfaces how fresh projection-backed data
// is, so operators understand they're looking at an eventually-consistent
// read model. Used wherever a panel/widget renders from a projection
// (status timeline, dashboard counters, list views, etc.).
//
// Lifecycle states keyed off age (seconds since the source event time):
//   ≤ 10s   → "live"        — green, pulsing dot
//   ≤ 60s   → "Updated Ns"  — neutral
//   ≤ 5m    → "Updated Nm"  — neutral
//   > 5m    → "stale Xm"    — amber, attention without panic
//
// Pass `lastEventAt` from the response payload's most recent event time
// (i.e. `MAX(occurred_at)` from the underlying read model, NOT the wall
// clock when the API responded).

export type DataFreshness = "live" | "fresh" | "stale";

const STALE_AFTER_SECONDS = 60 * 5; // 5 minutes
const FRESH_AFTER_SECONDS = 10;     // anything older than this is no longer "live"

export function DataFreshnessChip({
  lastEventAt,
  className,
}: {
  lastEventAt: string | Date | null | undefined;
  className?: string;
}) {
  // Re-tick every second so the chip updates without a parent re-render.
  // useState + setInterval keeps this self-contained — caller doesn't
  // need to thread a clock prop.
  const [, forceTick] = useState(0);
  useEffect(() => {
    const id = setInterval(() => forceTick((n) => n + 1), 1000);
    return () => clearInterval(id);
  }, []);

  if (!lastEventAt) return null;

  const eventTime = lastEventAt instanceof Date ? lastEventAt : new Date(lastEventAt);
  if (Number.isNaN(eventTime.getTime())) return null;

  const ageSeconds = Math.max(0, (Date.now() - eventTime.getTime()) / 1000);
  const { state, label } = describe(ageSeconds);

  const tone =
    state === "live"
      ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
      : state === "stale"
        ? "bg-[var(--color-amber-soft)] text-[var(--color-amber)]"
        : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06]";

  const dot =
    state === "live"
      ? "bg-[var(--color-success)]"
      : state === "stale"
        ? "bg-[var(--color-amber)]"
        : "bg-[var(--color-ink-400)]";

  return (
    <span
      title={`Last underlying event at ${eventTime.toLocaleString()}`}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-[3px] text-[10.5px] font-semibold uppercase tracking-[0.06em]",
        tone,
        className,
      )}
    >
      <span className="relative inline-flex h-1.5 w-1.5">
        <span className={cn("absolute inset-0 rounded-full", dot)} />
        {state === "live" && (
          <span className={cn("absolute inset-0 rounded-full animate-ping opacity-60", dot)} />
        )}
      </span>
      {label}
    </span>
  );
}

function describe(ageSeconds: number): { state: DataFreshness; label: string } {
  if (ageSeconds <= FRESH_AFTER_SECONDS) return { state: "live", label: "Live" };
  if (ageSeconds > STALE_AFTER_SECONDS) {
    const minutes = Math.round(ageSeconds / 60);
    return { state: "stale", label: `Stale ${minutes}m` };
  }
  if (ageSeconds < 60) return { state: "fresh", label: `Updated ${Math.round(ageSeconds)}s ago` };
  return { state: "fresh", label: `Updated ${Math.round(ageSeconds / 60)}m ago` };
}
