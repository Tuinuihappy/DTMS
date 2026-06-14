"use client";

import { History } from "lucide-react";
import { useEffect, useState } from "react";
import type { StatusHistoryEntry } from "@/lib/api/status-history";
import { DataFreshnessChip } from "./data-freshness-chip";
import { TimelineView, type TimelineEntry } from "./timeline-view";

// Phase P1 (b12) — Reusable drawer section that renders an aggregate's
// status-history projection. Used by Order / Job / Trip detail drawers
// via a single shared component so visual + accessibility behavior stays
// consistent across the three aggregates.
//
// Caller passes:
//   - title: section heading
//   - fetcher: () => Promise<StatusHistoryResponse> — already typed for
//     the specific endpoint (the API client exposes three).
//   - onClose-style key (entityId) drives a re-fetch when the parent
//     drawer switches to a new entity.

type FetchResult = {
  entries: StatusHistoryEntry[];
  lastEventAt: string | null;
};

export function StatusTimelineSection({
  entityId,
  title = "Status timeline",
  fetcher,
  liveEntry,
}: {
  entityId: string | null;
  title?: string;
  fetcher: (id: string) => Promise<FetchResult>;
  /**
   * Phase P1 — most recent entry received over SignalR. Caller owns the
   * hub subscription (so the same section component works regardless of
   * which hub: Order / Job / Trip). When this changes, the section
   * dedup-merges it into <c>entries</c> so the new row animates in
   * without a refetch.
   */
  liveEntry?: StatusHistoryEntry | null;
}) {
  const [data, setData] = useState<FetchResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Phase P1 — fold the latest hub-pushed entry into the data set whenever
  // the parent supplies a new one. Dedup by eventId so a fast refetch+push
  // race doesn't display the same row twice.
  useEffect(() => {
    if (!liveEntry) return;
    setData((prev) => {
      if (!prev) {
        return { entries: [liveEntry], lastEventAt: liveEntry.occurredAt };
      }
      if (prev.entries.some((e) => e.eventId === liveEntry.eventId)) {
        return prev; // already have this event
      }
      const merged = [liveEntry, ...prev.entries];
      const lastEventAt =
        !prev.lastEventAt || liveEntry.occurredAt > prev.lastEventAt
          ? liveEntry.occurredAt
          : prev.lastEventAt;
      return { entries: merged, lastEventAt };
    });
  }, [liveEntry]);

  useEffect(() => {
    if (!entityId) {
      setData(null);
      setError(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    fetcher(entityId)
      .then((d) => {
        if (!cancelled) setData(d);
      })
      .catch((e: Error) => {
        // Soft-fail — drawer keeps working without the timeline section.
        if (!cancelled) setError(e.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [entityId, fetcher]);

  // Hide section entirely when projection has no data AND fetch
  // succeeded — happens for very fresh entities before the projector
  // catches up, or for legacy entities not yet backfilled. We do NOT
  // hide on error so operators see something is wrong rather than
  // silently missing data.
  if (!loading && !error && (!data || data.entries.length === 0)) return null;

  return (
    <section>
      <h4 className="flex items-center justify-between gap-2 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        <span className="inline-flex items-center gap-1.5">
          <History className="h-3 w-3" strokeWidth={2.4} />
          {title}
        </span>
        {data?.lastEventAt && <DataFreshnessChip lastEventAt={data.lastEventAt} />}
      </h4>

      {error && (
        <div className="mt-3 rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
          Couldn&apos;t load timeline: {error}
        </div>
      )}

      <div className="mt-3">
        <TimelineView
          loading={loading && data === null}
          entries={(data?.entries ?? []).map(toTimelineEntry)}
          emptyMessage="No transitions recorded yet."
        />
      </div>
    </section>
  );
}

function toTimelineEntry(e: StatusHistoryEntry): TimelineEntry {
  const tone = toneFromStatus(e.toStatus);
  return {
    id: e.eventId,
    title: (
      <span className="inline-flex flex-wrap items-center gap-1.5">
        {e.fromStatus ? (
          <>
            <StatusChip label={e.fromStatus} tone="neutral" />
            <span className="text-[var(--color-ink-400)]">→</span>
            <StatusChip label={e.toStatus} tone={tone} />
          </>
        ) : (
          <>
            <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-400)]">
              entered
            </span>
            <StatusChip label={e.toStatus} tone={tone} />
          </>
        )}
      </span>
    ),
    subtitle: e.reason ?? undefined,
    occurredAt: e.occurredAt,
    dotTone: tone,
  };
}

function StatusChip({
  label,
  tone,
}: {
  label: string;
  tone: NonNullable<TimelineEntry["dotTone"]>;
}) {
  const palette: Record<NonNullable<TimelineEntry["dotTone"]>, string> = {
    success: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
    warning: "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
    error: "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]",
    info: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
    neutral:
      "bg-[var(--color-ink-100)] text-[var(--color-ink-700)] dark:bg-white/[0.06]",
  };
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-[2px] text-[10.5px] font-semibold uppercase tracking-[0.06em] ${palette[tone]}`}
    >
      {label}
    </span>
  );
}

// Map every status across Order / Job / Trip to a dot tone. Unknown
// statuses fall through to "neutral" so adding new states later is a
// soft-degrade not a build break.
function toneFromStatus(status: string): NonNullable<TimelineEntry["dotTone"]> {
  switch (status) {
    case "Completed":
    case "Delivered":
      return "success";
    case "Failed":
    case "Rejected":
    case "Cancelled":
      return "error";
    case "Held":
    case "Paused":
    case "Amended":
      return "warning";
    case "Confirmed":
    case "Planning":
    case "Planned":
    case "Created":
    case "Assigned":
    case "Committed":
    case "Dispatched":
    case "InProgress":
    case "Executing":
      return "info";
    case "PartiallyCompleted":
      return "warning";
    default:
      return "neutral";
  }
}
