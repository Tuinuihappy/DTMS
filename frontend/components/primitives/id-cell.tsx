"use client";

import { Check, Copy } from "lucide-react";
import { useEffect, useState } from "react";
import { copyText } from "@/lib/clipboard";
import { cn } from "@/lib/utils";

/**
 * Truncated GUID with the full value on hover and copy-on-click.
 *
 * Raw ids are what operators need to grep logs or query the DB, but a
 * 36-character GUID does not fit a compact ops table — and a bare
 * truncation would be useless, since the visible half can't be pasted
 * anywhere. So the cell shows the leading segment and keeps the whole
 * value one hover (title) or one click (clipboard) away.
 *
 * Two things callers depend on:
 *
 * - The click calls stopPropagation. Every table that uses this puts an
 *   open-drawer handler on the row itself, so without it a copy would
 *   also open the drawer.
 * - The copy-affordance reveal keys off `group-hover`, which means the
 *   containing row must carry Tailwind's `group` class. `DataRow` does.
 *
 * Copying goes through `copyText`, which falls back to execCommand —
 * the dashboard is also reached over plain http on a LAN IP, where
 * `navigator.clipboard` is unavailable.
 */
export function IdCell({
  id,
  label = "id",
  chars = 8,
  className,
}: {
  id: string;
  /** Noun used in the aria-label, e.g. "trip id" / "order id". */
  label?: string;
  /** Leading characters to show. */
  chars?: number;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const t = setTimeout(() => setCopied(false), 1200);
    return () => clearTimeout(t);
  }, [copied]);

  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        void copyText(id).then((ok) => ok && setCopied(true));
      }}
      title={`${id}\nClick to copy`}
      aria-label={`Copy ${label} ${id}`}
      className={cn(
        "-mx-1.5 flex items-center gap-1.5 rounded-md px-1.5 py-0.5",
        "font-mono text-[11px] tabular-nums text-[var(--color-ink-500)]",
        "transition-colors hover:bg-[var(--color-ink-100)]/70 hover:text-[var(--color-ink-800)]",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-brand-500)]",
        "dark:hover:bg-white/[0.06] dark:hover:text-white",
        className,
      )}
    >
      <span>{id.slice(0, chars)}</span>
      {copied ? (
        <Check
          className="h-3 w-3 shrink-0 text-[var(--color-mint-600,#16a34a)]"
          strokeWidth={2.4}
        />
      ) : (
        <Copy
          className="h-3 w-3 shrink-0 opacity-0 transition-opacity group-hover:opacity-50"
          strokeWidth={2.2}
        />
      )}
    </button>
  );
}
