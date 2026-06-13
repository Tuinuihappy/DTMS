"use client";

import { cn } from "@/lib/utils";

export type ReportWindow = "24h" | "7d" | "30d" | "90d";

export const WINDOW_HOURS: Record<ReportWindow, number> = {
  "24h": 24,
  "7d": 24 * 7,
  "30d": 24 * 30,
  "90d": 24 * 90,
};

export function buildWindowRange(w: ReportWindow) {
  const now = new Date();
  const to = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), now.getUTCHours() + 1),
  );
  const from = new Date(to.getTime() - WINDOW_HOURS[w] * 60 * 60 * 1000);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function fmtDuration(sec: number | null | undefined): string {
  if (sec == null || sec <= 0) return "—";
  if (sec < 60) return `${sec.toFixed(0)}s`;
  if (sec < 3600) return `${(sec / 60).toFixed(1)}m`;
  if (sec < 86400) return `${(sec / 3600).toFixed(1)}h`;
  return `${(sec / 86400).toFixed(1)}d`;
}

export function WindowToggle({
  value,
  onChange,
}: {
  value: ReportWindow;
  onChange: (w: ReportWindow) => void;
}) {
  const options: ReportWindow[] = ["24h", "7d", "30d", "90d"];
  return (
    <div className="inline-flex rounded-full bg-[var(--color-surface-soft)] p-1 dark:bg-white/[0.04]">
      {options.map((opt) => (
        <button
          key={opt}
          type="button"
          onClick={() => onChange(opt)}
          className={cn(
            "rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.06em] transition-colors",
            value === opt
              ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]"
              : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-900)]",
          )}
        >
          {opt}
        </button>
      ))}
    </div>
  );
}
