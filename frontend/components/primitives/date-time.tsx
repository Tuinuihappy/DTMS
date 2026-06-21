"use client";

import { useEffect, useState, type ReactNode } from "react";

import {
  DATETIME_EMPTY,
  formatDate,
  formatDateTime,
  formatDateTimeSeconds,
  formatRelative,
  formatTime,
  type DateInput,
} from "@/lib/datetime";

export type DateTimeVariant =
  | "date"
  | "time"
  | "datetime"
  | "datetime-seconds"
  | "relative";

const FORMATTERS: Record<DateTimeVariant, (v: DateInput) => string> = {
  date: formatDate,
  time: formatTime,
  datetime: formatDateTime,
  "datetime-seconds": formatDateTimeSeconds,
  relative: formatRelative,
};

interface DateTimeProps {
  value: DateInput;
  variant?: DateTimeVariant;
  /** Rendered on the server and during hydration. Default: `—`. */
  fallback?: ReactNode;
  /** Show absolute datetime on hover (only for `relative` variant). */
  showTooltip?: boolean;
  /** Auto-refresh interval in ms (only for `relative` variant). 0 = never. */
  refreshMs?: number;
  className?: string;
}

/**
 * SSR-safe date/time renderer. The server (and the first client render) emit
 * `fallback`; once mounted, the value is reformatted in the user's local
 * timezone. This avoids hydration mismatch when the server's timezone differs
 * from the user's browser.
 */
export function DateTime({
  value,
  variant = "datetime",
  fallback = DATETIME_EMPTY,
  showTooltip = true,
  refreshMs = 0,
  className,
}: DateTimeProps) {
  const [mounted, setMounted] = useState(false);
  const [, force] = useState(0);

  useEffect(() => {
    setMounted(true);
  }, []);

  useEffect(() => {
    if (variant !== "relative" || refreshMs <= 0) return;
    const id = setInterval(() => force((t) => t + 1), refreshMs);
    return () => clearInterval(id);
  }, [variant, refreshMs]);

  if (!mounted) {
    return <span className={className}>{fallback}</span>;
  }

  const text = FORMATTERS[variant](value);
  const tooltip =
    showTooltip && variant === "relative" ? formatDateTime(value) : undefined;

  return (
    <span className={className} title={tooltip}>
      {text}
    </span>
  );
}
