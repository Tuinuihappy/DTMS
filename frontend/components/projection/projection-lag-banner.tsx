"use client";

import { AlertTriangle, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useState } from "react";
import { cn } from "@/lib/utils";

// Phase P0 Day 5 — banner that surfaces projection lag to the operator
// when something is meaningfully delayed (e.g. the outbox processor is
// behind, or RabbitMQ is being slow). Hidden when lag is acceptable.
//
// Threshold defaults to 60s — chosen so the normal outbox poll cycle
// (5s) + projector lag (typically < 1s after publish) never triggers,
// while genuine backpressure becomes visible within ~1 minute.

const DEFAULT_THRESHOLD_SECONDS = 60;

export function ProjectionLagBanner({
  lagSeconds,
  thresholdSeconds = DEFAULT_THRESHOLD_SECONDS,
  className,
}: {
  /** Current observed lag — typically from the /admin/projections feed. */
  lagSeconds: number | null | undefined;
  /** Show only when lag is above this many seconds. */
  thresholdSeconds?: number;
  className?: string;
}) {
  // Operator can dismiss the banner for the rest of this session. It
  // re-appears if lag drops then climbs above threshold again — that's
  // a new "event" and worth showing.
  const [dismissedAtLag, setDismissedAtLag] = useState<number | null>(null);

  const visible =
    typeof lagSeconds === "number" &&
    Number.isFinite(lagSeconds) &&
    lagSeconds >= thresholdSeconds &&
    (dismissedAtLag === null || lagSeconds < dismissedAtLag * 0.5);

  return (
    <AnimatePresence>
      {visible && typeof lagSeconds === "number" && (
        <motion.div
          initial={{ opacity: 0, y: -8 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -8 }}
          transition={{ duration: 0.25 }}
          role="status"
          aria-live="polite"
          className={cn(
            "flex items-center gap-3 rounded-xl border border-[var(--color-amber)]/40",
            "bg-[var(--color-amber-soft)] px-4 py-2.5",
            "text-[12.5px] text-[var(--color-amber)]",
            className,
          )}
        >
          <AlertTriangle className="h-4 w-4 shrink-0" strokeWidth={2.4} />
          <span className="flex-1 font-medium">
            Projection updates are delayed — data may be{" "}
            <span className="font-mono font-semibold">
              {Math.round(lagSeconds)}s
            </span>{" "}
            behind. Reads are safe; recent edits may take a moment to appear.
          </span>
          <button
            type="button"
            onClick={() => setDismissedAtLag(lagSeconds)}
            className="rounded-full p-1 hover:bg-[var(--color-amber)]/10"
            aria-label="Dismiss"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.4} />
          </button>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
