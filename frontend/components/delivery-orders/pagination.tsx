"use client";

import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight, Loader2 } from "lucide-react";
import { motion } from "motion/react";
import { cn } from "@/lib/utils";

const PAGE_SIZES = [10, 25, 50, 100] as const;
export type PageSize = (typeof PAGE_SIZES)[number];
export type PaginationMode = "paged" | "infinite";

export function Pagination({
  total,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  mode,
  onModeChange,
}: {
  total: number;
  page: number;
  pageSize: PageSize;
  onPageChange: (p: number) => void;
  onPageSizeChange: (s: PageSize) => void;
  mode?: PaginationMode;
  onModeChange?: (m: PaginationMode) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const startIdx = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const endIdx = Math.min(total, page * pageSize);
  const canPrev = page > 1;
  const canNext = page < totalPages;

  return (
    <div className="flex flex-col items-center justify-between gap-3 px-4 py-3 sm:flex-row">
      <div className="flex items-center gap-3 text-[11.5px] text-[var(--color-ink-500)]">
        <span className="font-mono tabular-nums">
          <span className="text-[var(--color-ink-900)] font-semibold">
            {startIdx.toLocaleString("en-US")}–{endIdx.toLocaleString("en-US")}
          </span>{" "}
          of {total.toLocaleString("en-US")}
        </span>
        <span className="hidden sm:inline h-3 w-px bg-[var(--color-ink-200)]/60 dark:bg-white/10" />
        <label className="hidden sm:flex items-center gap-1.5">
          <span className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
            Per page
          </span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value) as PageSize)}
            className={cn(
              "rounded-md bg-white/60 px-2 py-1 font-mono text-[11.5px] font-semibold tabular-nums",
              "border border-white/70 text-[var(--color-ink-900)] backdrop-blur-md",
              "focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 focus:border-[var(--color-brand-500)]/30",
              "dark:bg-white/[0.05] dark:border-white/10",
            )}
          >
            {PAGE_SIZES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </label>
        {mode && onModeChange && (
          <>
            <span className="hidden sm:inline h-3 w-px bg-[var(--color-ink-200)]/60 dark:bg-white/10" />
            <ModeToggle mode={mode} onModeChange={onModeChange} />
          </>
        )}
      </div>

      <div className="flex items-center gap-1">
        <PagerBtn onClick={() => onPageChange(1)} disabled={!canPrev} title="First page">
          <ChevronsLeft className="h-3.5 w-3.5" strokeWidth={2.4} />
        </PagerBtn>
        <PagerBtn onClick={() => onPageChange(page - 1)} disabled={!canPrev} title="Previous">
          <ChevronLeft className="h-3.5 w-3.5" strokeWidth={2.4} />
        </PagerBtn>

        <div className="flex items-center gap-0.5 px-1">
          {pageWindow(page, totalPages).map((p, i) =>
            p === "…" ? (
              <span
                key={`gap-${i}`}
                className="px-1 text-[11.5px] text-[var(--color-ink-400)]"
              >
                ···
              </span>
            ) : (
              <motion.button
                key={p}
                type="button"
                onClick={() => onPageChange(p)}
                whileTap={{ scale: 0.92 }}
                className={cn(
                  "min-w-7 h-7 rounded-md px-1.5 text-[11.5px] font-mono font-semibold tabular-nums transition-all",
                  p === page
                    ? "bg-[var(--color-brand-900)] text-white shadow-[0_6px_14px_-6px_rgba(15,23,42,0.45)] dark:bg-[var(--color-brand-500)]"
                    : "text-[var(--color-ink-600)] hover:bg-white/60 dark:hover:bg-white/[0.06]",
                )}
              >
                {p}
              </motion.button>
            ),
          )}
        </div>

        <PagerBtn onClick={() => onPageChange(page + 1)} disabled={!canNext} title="Next">
          <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.4} />
        </PagerBtn>
        <PagerBtn
          onClick={() => onPageChange(totalPages)}
          disabled={!canNext}
          title="Last page"
        >
          <ChevronsRight className="h-3.5 w-3.5" strokeWidth={2.4} />
        </PagerBtn>
      </div>
    </div>
  );
}

function PagerBtn({
  onClick,
  disabled,
  title,
  children,
}: {
  onClick: () => void;
  disabled?: boolean;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      title={title}
      disabled={disabled}
      whileTap={!disabled ? { scale: 0.92 } : {}}
      className={cn(
        "grid h-7 w-7 place-items-center rounded-md transition-colors",
        "text-[var(--color-ink-600)] hover:bg-white/60 dark:hover:bg-white/[0.06]",
        "disabled:opacity-30 disabled:cursor-not-allowed",
      )}
    >
      {children}
    </motion.button>
  );
}

export function InfiniteFooter({
  shown,
  total,
  loading,
  onLoadMore,
  mode,
  onModeChange,
}: {
  shown: number;
  total: number;
  loading: boolean;
  onLoadMore: () => void;
  mode: PaginationMode;
  onModeChange: (m: PaginationMode) => void;
}) {
  const canLoadMore = shown < total && !loading;
  return (
    <div className="flex flex-col items-center justify-between gap-3 px-4 py-3 sm:flex-row">
      <div className="flex items-center gap-3 text-[11.5px] text-[var(--color-ink-500)]">
        <span className="font-mono tabular-nums">
          <span className="text-[var(--color-ink-900)] font-semibold">
            {shown.toLocaleString("en-US")}
          </span>{" "}
          of {total.toLocaleString("en-US")}
        </span>
        <span className="hidden sm:inline h-3 w-px bg-[var(--color-ink-200)]/60 dark:bg-white/10" />
        <ModeToggle mode={mode} onModeChange={onModeChange} />
      </div>

      <motion.button
        type="button"
        onClick={onLoadMore}
        disabled={!canLoadMore}
        whileTap={canLoadMore ? { scale: 0.96 } : {}}
        className={cn(
          "inline-flex items-center gap-1.5 rounded-full px-3.5 py-2 text-[12px] font-semibold",
          "bg-[var(--color-brand-900)] text-white shadow-[0_10px_28px_-12px_rgba(15,23,42,0.45)]",
          "transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)]",
          "dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]",
          "disabled:opacity-40 disabled:cursor-not-allowed disabled:shadow-none",
        )}
      >
        {loading && <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />}
        {shown >= total ? "All loaded" : loading ? "Loading…" : "Load more"}
      </motion.button>
    </div>
  );
}

function ModeToggle({
  mode,
  onModeChange,
}: {
  mode: PaginationMode;
  onModeChange: (m: PaginationMode) => void;
}) {
  return (
    <div
      className={cn(
        "inline-flex rounded-md p-0.5 text-[10.5px] font-semibold uppercase tracking-[0.08em]",
        "bg-white/60 border border-white/70 backdrop-blur-md",
        "dark:bg-white/[0.05] dark:border-white/10",
      )}
    >
      {(["paged", "infinite"] as const).map((m) => (
        <button
          key={m}
          type="button"
          onClick={() => onModeChange(m)}
          className={cn(
            "rounded-[3px] px-2 py-0.5 transition-all",
            mode === m
              ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
              : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-900)]",
          )}
        >
          {m === "paged" ? "Pages" : "Scroll"}
        </button>
      ))}
    </div>
  );
}

// Pager window: always show 1 and last; show ±2 around current, with
// "…" gaps. Keeps the bar to ≤7 buttons regardless of total page count.
function pageWindow(page: number, total: number): Array<number | "…"> {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const out: Array<number | "…"> = [1];
  const start = Math.max(2, page - 2);
  const end = Math.min(total - 1, page + 2);
  if (start > 2) out.push("…");
  for (let i = start; i <= end; i++) out.push(i);
  if (end < total - 1) out.push("…");
  out.push(total);
  return out;
}
