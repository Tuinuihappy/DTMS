"use client";

import { ArrowDown, CircleAlert, Repeat2, User } from "lucide-react";
import { useEffect, useState } from "react";
import {
  getTripRetryHistory,
  type TripChainEntryDto,
  type TripRetryHistoryDto,
} from "@/lib/api/trips";
import { cn } from "@/lib/utils";
import { DateTime } from "@/components/primitives/date-time";
import { TripStatusBadge } from "./badges";

/**
 * Vertical timeline of every retry attempt for a Trip's group on the
 * same DeliveryOrder. Each attempt shows status + outcome + the audit
 * trigger that produced it (operator action, reason, timestamp).
 * Clicking an attempt's "open" button asks the parent to swap the
 * drawer to that attempt without closing-then-reopening.
 */
export function RetryHistoryPanel({
  tripId,
  onOpenAttempt,
}: {
  tripId: string;
  onOpenAttempt?: (attemptTripId: string) => void;
}) {
  const [data, setData] = useState<TripRetryHistoryDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    getTripRetryHistory(tripId)
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
  }, [tripId]);

  if (error) {
    return (
      <div className="rounded-xl border border-dashed border-[var(--color-ink-100)] px-4 py-3 text-[11.5px] text-[var(--color-ink-500)] dark:border-white/10">
        Could not load retry history: {error}
      </div>
    );
  }

  if (loading && !data) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 2 }).map((_, i) => (
          <div
            key={i}
            className="h-16 animate-pulse rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.05]"
          />
        ))}
      </div>
    );
  }

  if (!data || data.attempts.length === 0) return null;

  // First-dispatch-only chains aren't a "history" — surface as plain note.
  if (data.totalAttempts === 1) {
    return (
      <div className="rounded-xl border border-[var(--color-ink-100)] bg-[var(--color-ink-100)]/30 px-3 py-2.5 text-[11.5px] text-[var(--color-ink-500)] dark:border-white/10 dark:bg-white/[0.03]">
        This is the first dispatch attempt — no retries yet.
      </div>
    );
  }

  return (
    <ol className="relative space-y-3 pl-5">
      {/* vertical rail */}
      <span
        aria-hidden
        className="absolute left-[7px] top-3 bottom-3 w-px bg-[var(--color-ink-100)] dark:bg-white/10"
      />
      {data.attempts.map((attempt, idx) => (
        <AttemptRow
          key={attempt.tripId}
          attempt={attempt}
          showConnector={idx < data.attempts.length - 1}
          onOpen={onOpenAttempt}
        />
      ))}
    </ol>
  );
}

function AttemptRow({
  attempt,
  showConnector,
  onOpen,
}: {
  attempt: TripChainEntryDto;
  showConnector: boolean;
  onOpen?: (tripId: string) => void;
}) {
  const isFailed = attempt.status === "Failed";
  const isCompleted = attempt.status === "Completed";
  const isCancelled = attempt.status === "Cancelled";

  return (
    <li className="relative">
      {/* dot on the rail */}
      <span
        aria-hidden
        className={cn(
          "absolute -left-[18px] top-1 inline-flex h-[14px] w-[14px] items-center justify-center rounded-full ring-4 font-mono text-[9px] font-bold",
          attempt.isCurrent
            ? "bg-[var(--color-brand-500)] text-white ring-[var(--color-pastel-sky)]"
            : isCompleted
              ? "bg-[var(--color-success)] text-white ring-[var(--color-success-soft)]"
              : isFailed
                ? "bg-[var(--color-coral)] text-white ring-[var(--color-coral-soft)]"
                : isCancelled
                  ? "bg-[var(--color-ink-400)] text-white ring-[var(--color-ink-100)]"
                  : "bg-[var(--color-surface)] text-[var(--color-ink-700)] ring-[var(--color-ink-100)] dark:ring-white/10",
        )}
      >
        {attempt.attemptNumber}
      </span>

      <div
        className={cn(
          "ml-1 rounded-xl px-3 py-2.5",
          attempt.isCurrent
            ? "bg-[var(--color-pastel-sky)]/40 ring-1 ring-[var(--color-brand-500)]/30"
            : "bg-[var(--color-ink-100)]/30 dark:bg-white/[0.03]",
        )}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <span className="text-[11px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                Attempt {attempt.attemptNumber}
              </span>
              <TripStatusBadge status={attempt.status} />
              {attempt.isCurrent && (
                <span className="rounded-full bg-[var(--color-brand-500)] px-1.5 py-[2px] text-[9px] font-bold uppercase tracking-[0.06em] text-white">
                  viewing
                </span>
              )}
            </div>
            <div className="mt-1 flex items-center gap-3 text-[11px] text-[var(--color-ink-500)]">
              {attempt.vendorOrderKey && (
                <span className="font-mono">vendor #{attempt.vendorOrderKey}</span>
              )}
              <DateTime value={attempt.createdAt} />
            </div>
            {attempt.failureReason && (
              <div className="mt-1.5 flex items-start gap-1 text-[11px] text-[var(--color-coral)]">
                <CircleAlert className="mt-0.5 h-3 w-3 flex-shrink-0" strokeWidth={2.4} />
                <span>{attempt.failureReason}</span>
              </div>
            )}
          </div>
          {!attempt.isCurrent && onOpen && (
            <button
              type="button"
              onClick={() => onOpen(attempt.tripId)}
              className="rounded-full bg-[var(--color-ink-100)] px-2.5 py-1 text-[10.5px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-[var(--color-ink-200)] dark:bg-white/[0.06] dark:hover:bg-white/[0.1]"
            >
              Open
            </button>
          )}
        </div>
      </div>

      {/* "Retried by …" connector pointing to the NEXT attempt below */}
      {showConnector && attempt.retryTrigger === null && (
        <NextRetryHint attempt={attempt} />
      )}
    </li>
  );
}

function NextRetryHint({ attempt }: { attempt: TripChainEntryDto }) {
  // Find the NEXT attempt by scanning siblings — but RetryTrigger lives
  // on the next attempt, not this one. So this is just a visual cue
  // (handled by the rail itself); no inline text needed.
  return null;
}

// Note: each attempt's RetryTrigger is "the audit that brought us TO
// this attempt" — so when attempt 2 has a trigger, it means it was a
// retry of attempt 1. We render it inline as the row's metadata block.
export function RetryTriggerLine({
  attempt,
}: {
  attempt: TripChainEntryDto;
}) {
  if (!attempt.retryTrigger) return null;
  const trig = attempt.retryTrigger;
  return (
    <div className="ml-1 flex items-start gap-2 px-3 py-1.5 text-[10.5px] text-[var(--color-ink-500)]">
      <ArrowDown className="mt-0.5 h-3 w-3 flex-shrink-0" strokeWidth={2.4} />
      <span className="flex-1">
        <span className="font-semibold text-[var(--color-pastel-lavender-ink)]">
          {trig.retrySource}
        </span>
        {" retry"}
        {trig.retriedBy && (
          <>
            {" by "}
            <span className="inline-flex items-center gap-0.5 font-mono">
              <User className="h-2.5 w-2.5" strokeWidth={2.4} />
              {trig.retriedBy}
            </span>
          </>
        )}
        {trig.retryReason && (
          <span className="block text-[10.5px] italic">&quot;{trig.retryReason}&quot;</span>
        )}
      </span>
    </div>
  );
}
