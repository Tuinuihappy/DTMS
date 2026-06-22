"use client";

import { ChevronRight, RotateCcw, Search, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  getJobById,
  getJobsQueue,
  isJobRetriable,
  retryJob,
  type JobDto,
  type JobStatus,
  type JobsQueueResult,
  type JobsQueueSortKey,
} from "@/lib/api/jobs";
import { ToastProvider, useToast } from "@/components/delivery-orders/toast";
import { StatusTimelineSection } from "@/components/projection/status-timeline-section";
import type { StatusHistoryEntry } from "@/lib/api/status-history";
import { getJobStatusHistory } from "@/lib/api/status-history";
import { useJobQueueSubscription, useJobSubscription } from "@/lib/realtime/hubs/job-hub";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  DataTableShell,
  SortableTh,
  TableEmptyState,
  TableSkeleton,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table";
import { JobStatusBadge } from "./badges";
import { cn } from "@/lib/utils";

// Phase b10-frontend.2 — operator queue across every order. Three tabs
// drive the status-set filter:
//   Failed — terminal, retriable via Phase b8 /retry endpoint
//   Stuck  — pre-vendor states (Created/Assigned/Committed) so ops can
//            see Jobs that never made it into the dispatch loop
//   All    — diagnostic; no filter
type TabKey = "failed" | "stuck" | "all";

const TAB_STATUSES: Record<TabKey, JobStatus[]> = {
  failed: ["Failed"],
  stuck: ["Created", "Assigned", "Committed"],
  all: [],
};

const TAB_LABELS: Record<TabKey, string> = {
  failed: "Failed",
  stuck: "Stuck",
  all: "All",
};

const PAGE_SIZE = 20;

export function JobsExperience() {
  return (
    <ToastProvider>
      <JobsExperienceInner />
    </ToastProvider>
  );
}

function JobsExperienceInner() {
  const [tab, setTab] = useState<TabKey>("all");
  const [page, setPage] = useState(1);
  // Backend Phase 1 (B2) — operator workflow asked for "show me jobs
  // retried the most" and "the most urgent (SLA) ones first". Default
  // is createdAt desc which preserves the previous newest-first ordering.
  const [sortBy, setSortBy] = useState<JobsQueueSortKey>("createdAt");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("desc");
  const [data, setData] = useState<JobsQueueResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openJobId, setOpenJobId] = useState<string | null>(null);

  const fetch = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      try {
        const res = await getJobsQueue({
          statuses: TAB_STATUSES[tab],
          page,
          pageSize: PAGE_SIZE,
          sortBy,
          sortDir,
        });
        if (!signal?.aborted) setData(res);
      } catch (e) {
        if (!signal?.aborted) setError((e as Error).message);
      } finally {
        if (!signal?.aborted) setLoading(false);
      }
    },
    [tab, page, sortBy, sortDir],
  );

  const handleSortChange = useCallback(
    (col: JobsQueueSortKey) => {
      if (sortBy === col) {
        setSortDir((d) => (d === "asc" ? "desc" : "asc"));
      } else {
        setSortBy(col);
        // Most operator workflows benefit from desc on first click
        // (newest, biggest attempt count, most urgent SLA first).
        // status is the exception: asc reads alphabetically which is
        // more useful for the "group by state" mental model.
        setSortDir(col === "status" ? "asc" : "desc");
      }
    },
    [sortBy],
  );

  useEffect(() => {
    const ctl = new AbortController();
    void fetch(ctl.signal);
    return () => ctl.abort();
  }, [fetch]);

  // Phase P3 quick-win — JobStatusHistoryProjector pushes a queue hint
  // via JobHub.JobUpdated on every job status change. Debounce-refetch
  // so a burst (e.g. retry-storm during ops cutover) doesn't trigger N
  // back-to-back fetches inside the batcher window.
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useJobQueueSubscription({
    JobUpdated: () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
      debounceRef.current = setTimeout(() => {
        void fetch();
      }, 500);
    },
  });
  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  // Reset to page 1 on tab or sort change so the operator doesn't land
  // on an empty page after switching to a sparser status set.
  useEffect(() => {
    setPage(1);
  }, [tab, sortBy, sortDir]);

  const total = data?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-5">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Planning jobs queue
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Failed jobs can be retried — a new Trip is dispatched on the same envelope anchor.
          </p>
        </div>
        <div className="text-right">
          <div className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
            {TAB_LABELS[tab]} · matching
          </div>
          <div className="font-mono text-[1.4rem] font-semibold tabular-nums text-[var(--color-ink-900)]">
            {total.toLocaleString("en-US")}
          </div>
        </div>
      </header>

      <Tabs current={tab} onChange={setTab} />

      {error && (
        <div className="rounded-xl bg-[var(--color-coral-soft)] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)]">
          {error}
        </div>
      )}

      <JobsTable
        jobs={data?.items ?? []}
        loading={loading && data === null}
        sortBy={sortBy}
        sortDir={sortDir}
        onSortChange={handleSortChange}
        onOpen={(id) => setOpenJobId(id)}
        onRetried={() => fetch()}
      />

      {pageCount > 1 && (
        <Pagination
          page={page}
          pageCount={pageCount}
          onChange={(p) => setPage(p)}
        />
      )}

      <JobDetailDrawer
        jobId={openJobId}
        onClose={() => setOpenJobId(null)}
        onRetried={() => {
          setOpenJobId(null);
          void fetch();
        }}
      />
    </div>
  );
}

function Tabs({
  current,
  onChange,
}: {
  current: TabKey;
  onChange: (t: TabKey) => void;
}) {
  const tabs: TabKey[] = ["all", "stuck", "failed"];
  return (
    <div className="inline-flex rounded-full bg-[var(--color-surface-soft)] p-1 dark:bg-white/[0.04]">
      {tabs.map((t) => (
        <button
          key={t}
          type="button"
          onClick={() => onChange(t)}
          className={cn(
            "rounded-full px-4 py-1.5 text-[12px] font-semibold uppercase tracking-[0.06em] transition-colors",
            current === t
              ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]"
              : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-900)]",
          )}
        >
          {TAB_LABELS[t]}
        </button>
      ))}
    </div>
  );
}

function JobsTable({
  jobs,
  loading,
  sortBy,
  sortDir,
  onSortChange,
  onOpen,
  onRetried,
}: {
  jobs: JobDto[];
  loading: boolean;
  sortBy: JobsQueueSortKey;
  sortDir: "asc" | "desc";
  onSortChange: (col: JobsQueueSortKey) => void;
  onOpen: (id: string) => void;
  onRetried: () => void;
}) {
  if (loading) {
    return <TableSkeleton label="Loading jobs…" />;
  }

  if (jobs.length === 0) {
    return (
      <TableEmptyState
        variant="no-filter-match"
        icon={Search}
        title="No jobs match the current filter"
        body="Try a different tab or wait for new jobs to flow through the queue."
      />
    );
  }

  return (
    <DataTableShell>
      <DataTableHead>
        <TableTh density="compact">Job</TableTh>
        <TableTh density="compact">Order</TableTh>
        <SortableTh
          col="status"
          sortBy={sortBy}
          sortDir={sortDir}
          onSort={onSortChange}
          density="compact"
        >
          Status
        </SortableTh>
        <SortableTh
          col="attemptNumber"
          sortBy={sortBy}
          sortDir={sortDir}
          onSort={onSortChange}
          align="right"
          density="compact"
        >
          Attempt
        </SortableTh>
        <TableTh density="compact">Pattern</TableTh>
        <TableTh density="compact">Reason</TableTh>
        <TableTh density="compact" align="right" aria-label="Actions">
          <span className="sr-only">Actions</span>
        </TableTh>
      </DataTableHead>
      <DataTableBody>
        {jobs.map((j, i) => (
          <JobRow
            key={j.id}
            job={j}
            row={i}
            onOpen={onOpen}
            onRetried={onRetried}
          />
        ))}
      </DataTableBody>
    </DataTableShell>
  );
}

function JobRow({
  job,
  row,
  onOpen,
  onRetried,
}: {
  job: JobDto;
  row: number;
  onOpen: (id: string) => void;
  onRetried: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const toast = useToast();
  const retriable = isJobRetriable(job.status);

  return (
    <DataRow delayIndex={row} onClick={() => onOpen(job.id)}>
      <TableTd density="compact" className="font-mono text-[11.5px]">
        <span className="text-[var(--color-ink-900)]">{job.id.slice(0, 8)}…</span>
        {job.groupIndex != null && (
          <div className="mt-0.5 text-[10.5px] text-[var(--color-ink-400)]">
            G{job.groupIndex}
          </div>
        )}
      </TableTd>
      <TableTd density="compact" className="font-mono text-[11.5px]">
        <Link
          href={`/delivery-orders/list?orderId=${job.deliveryOrderId}`}
          onClick={(e) => e.stopPropagation()}
          className="text-[var(--color-brand-900)] underline-offset-4 hover:underline dark:text-[var(--color-brand-500)]"
        >
          {job.deliveryOrderId.slice(0, 8)}…
        </Link>
      </TableTd>
      <TableTd density="compact">
        <JobStatusBadge status={job.status} />
      </TableTd>
      <TableTd
        density="compact"
        align="right"
        className="font-mono tabular-nums text-[11.5px] text-[var(--color-ink-800)]"
      >
        {job.attemptNumber > 1 ? (
          <span className="inline-flex items-center gap-1">
            {job.attemptNumber}
            <span className="rounded bg-[var(--color-pastel-lavender)] px-1 py-[1px] text-[9px] font-bold uppercase tracking-[0.06em] text-[var(--color-pastel-lavender-ink)]">
              retry
            </span>
          </span>
        ) : (
          job.attemptNumber
        )}
      </TableTd>
      <TableTd density="compact" className="text-[11.5px] text-[var(--color-ink-700)]">
        {job.pattern}
      </TableTd>
      <TableTd
        density="compact"
        className="max-w-[280px] truncate text-[11.5px] text-[var(--color-ink-500)]"
      >
        <span title={job.failureReason ?? undefined}>{job.failureReason ?? "—"}</span>
      </TableTd>
      <TableTd density="compact" align="right">
        {retriable && (
          <button
            type="button"
            disabled={busy}
            onClick={async (e) => {
              e.stopPropagation();
              setBusy(true);
              try {
                const updated = await retryJob(job.id);
                const ok = updated.status !== "Failed";
                toast.push({
                  tone: ok ? "success" : "error",
                  message: ok
                    ? `Job ${job.id.slice(0, 8)}… re-dispatched (${updated.status})`
                    : `Retry failed: ${updated.failureReason ?? "unknown"}`,
                });
                onRetried();
              } catch (err) {
                toast.push({
                  tone: "error",
                  message: `Retry failed: ${(err as Error).message}`,
                });
              } finally {
                setBusy(false);
              }
            }}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[11px] font-semibold transition-all",
              busy
                ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
                : "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80",
            )}
          >
            <RotateCcw className={cn("h-3 w-3", busy && "animate-spin")} strokeWidth={2.4} />
            Retry
          </button>
        )}
      </TableTd>
    </DataRow>
  );
}

function Pagination({
  page,
  pageCount,
  onChange,
}: {
  page: number;
  pageCount: number;
  onChange: (p: number) => void;
}) {
  return (
    <div className="flex items-center justify-between text-[12px] text-[var(--color-ink-500)]">
      <span>
        Page <span className="font-semibold text-[var(--color-ink-900)]">{page}</span> of{" "}
        {pageCount}
      </span>
      <div className="inline-flex gap-2">
        <button
          type="button"
          disabled={page <= 1}
          onClick={() => onChange(page - 1)}
          className={cn(
            "rounded-full px-3 py-1.5 text-[11.5px] font-semibold transition-colors",
            page <= 1
              ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
              : "bg-[var(--color-surface-soft)] text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] dark:bg-white/[0.04] dark:hover:bg-white/[0.08]",
          )}
        >
          Prev
        </button>
        <button
          type="button"
          disabled={page >= pageCount}
          onClick={() => onChange(page + 1)}
          className={cn(
            "rounded-full px-3 py-1.5 text-[11.5px] font-semibold transition-colors",
            page >= pageCount
              ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
              : "bg-[var(--color-surface-soft)] text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] dark:bg-white/[0.04] dark:hover:bg-white/[0.08]",
          )}
        >
          Next
        </button>
      </div>
    </div>
  );
}

function JobDetailDrawer({
  jobId,
  onClose,
  onRetried,
}: {
  jobId: string | null;
  onClose: () => void;
  onRetried: () => void;
}) {
  const [data, setData] = useState<JobDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retrying, setRetrying] = useState(false);
  const [liveTimelineEntry, setLiveTimelineEntry] = useState<StatusHistoryEntry | null>(null);
  const toast = useToast();

  // Phase P1 — JobHub live timeline updates while the drawer is open.
  useJobSubscription(jobId, {
    TimelineUpdated: (entry) => setLiveTimelineEntry(entry as StatusHistoryEntry),
  });

  useEffect(() => {
    if (!jobId) {
      setData(null);
      setError(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    getJobById(jobId)
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
  }, [jobId]);

  useEffect(() => {
    if (!jobId) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [jobId, onClose]);

  return (
    <AnimatePresence>
      {jobId && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.22 }}
            onClick={onClose}
            className="fixed inset-0 z-40 bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
          />
          <motion.aside
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 340, damping: 38 }}
            className="fixed right-0 top-0 z-50 flex h-full w-full flex-col bg-[var(--color-surface)] shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)] sm:w-[min(520px,100vw)]"
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-6 py-5 dark:border-white/[0.06]">
              <div className="min-w-0 flex-1">
                <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                  Planning job
                </div>
                {loading || !data ? (
                  <div className="mt-2 h-7 w-48 animate-pulse rounded-md bg-[var(--color-ink-100)]" />
                ) : (
                  <>
                    <h2 className="font-mono mt-1 truncate text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                      {data.id.slice(0, 8)}…
                    </h2>
                    <div className="mt-2 flex flex-wrap items-center gap-2">
                      <JobStatusBadge status={data.status} size="md" />
                      {data.attemptNumber > 1 && (
                        <span className="rounded bg-[var(--color-pastel-lavender)] px-1.5 py-[2px] text-[10px] font-bold uppercase tracking-[0.06em] text-[var(--color-pastel-lavender-ink)]">
                          attempt {data.attemptNumber}
                        </span>
                      )}
                    </div>
                  </>
                )}
              </div>
              <button
                type="button"
                onClick={onClose}
                className="rounded-full p-2 text-[var(--color-ink-500)] hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                aria-label="Close"
              >
                <X className="h-4 w-4" strokeWidth={2.4} />
              </button>
            </header>

            <div className="flex-1 overflow-y-auto px-6 py-5">
              {error && (
                <div className="rounded-xl bg-[var(--color-coral-soft)] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)]">
                  {error}
                </div>
              )}
              {data && (
                <div className="space-y-5 text-[13px]">
                  <Field label="Delivery order">
                    <Link
                      href={`/delivery-orders/list?orderId=${data.deliveryOrderId}`}
                      className="font-mono text-[12px] text-[var(--color-brand-900)] underline-offset-4 hover:underline dark:text-[var(--color-brand-500)]"
                    >
                      {data.deliveryOrderId}
                    </Link>
                  </Field>
                  <Field label="Pattern">{data.pattern}</Field>
                  {data.groupIndex != null && (
                    <Field label="Group index">{data.groupIndex}</Field>
                  )}
                  {data.tripId && (
                    <Field label="Bound trip">
                      <span className="font-mono text-[12px]">{data.tripId}</span>
                    </Field>
                  )}
                  {data.vendorOrderKey && (
                    <Field label="Vendor order key">
                      <span className="font-mono text-[12px]">{data.vendorOrderKey}</span>
                    </Field>
                  )}
                  {data.failureReason && (
                    <Field label="Failure reason">
                      <span className="text-[var(--color-coral)]">{data.failureReason}</span>
                    </Field>
                  )}
                  <Field label="Transport mode">{data.transportMode ?? "—"}</Field>
                  <Field label="Required capability">{data.requiredCapability ?? "—"}</Field>

                  {/* Phase P1 — structured status-history timeline with
                      realtime JobHub updates. */}
                  <StatusTimelineSection
                    entityId={data.id}
                    fetcher={getJobStatusHistory}
                    liveEntry={liveTimelineEntry}
                  />
                </div>
              )}
            </div>

            {data && isJobRetriable(data.status) && (
              <footer className="border-t border-[var(--color-ink-100)] px-6 py-4 dark:border-white/[0.06]">
                <button
                  type="button"
                  disabled={retrying}
                  onClick={async () => {
                    setRetrying(true);
                    try {
                      const updated = await retryJob(data.id);
                      const ok = updated.status !== "Failed";
                      toast.push({
                        tone: ok ? "success" : "error",
                        message: ok
                          ? `Job re-dispatched (${updated.status})`
                          : `Retry failed: ${updated.failureReason ?? "unknown"}`,
                      });
                      onRetried();
                    } catch (e) {
                      toast.push({
                        tone: "error",
                        message: `Retry failed: ${(e as Error).message}`,
                      });
                    } finally {
                      setRetrying(false);
                    }
                  }}
                  className={cn(
                    "inline-flex w-full items-center justify-center gap-2 rounded-full px-4 py-2.5 text-[13px] font-semibold transition-all",
                    retrying
                      ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
                      : "bg-[var(--color-brand-900)] text-white hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]",
                  )}
                >
                  <RotateCcw
                    className={cn("h-4 w-4", retrying && "animate-spin")}
                    strokeWidth={2.4}
                  />
                  Retry job
                </button>
              </footer>
            )}
          </motion.aside>
        </>
      )}
    </AnimatePresence>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <div className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        {label}
      </div>
      <div className="mt-1 text-[13px] text-[var(--color-ink-900)]">{children}</div>
    </div>
  );
}
