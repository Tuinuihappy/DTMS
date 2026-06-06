"use client";

import { Check, ChevronDown, ChevronRight, Copy } from "lucide-react";
import { useState } from "react";
import { cn } from "@/lib/utils";

// Collapsible JSON viewer for vendor request / final snapshots. Default
// collapsed because the blobs can be megabytes — operators only expand
// them for compliance / debugging.
export function SnapshotInspector({
  label,
  payload,
}: {
  label: string;
  payload: string | null;
}) {
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);

  const formatted = useFormattedJson(payload);
  const size = payload?.length ?? 0;
  const sizeLabel =
    size === 0
      ? "empty"
      : size < 1024
        ? `${size}B`
        : size < 1024 * 1024
          ? `${(size / 1024).toFixed(1)}KB`
          : `${(size / (1024 * 1024)).toFixed(2)}MB`;

  const handleCopy = async () => {
    if (!payload) return;
    try {
      await navigator.clipboard.writeText(payload);
      setCopied(true);
      setTimeout(() => setCopied(false), 1600);
    } catch {
      // clipboard may be unavailable in unsecure contexts; silent fail
    }
  };

  return (
    <div className="rounded-xl border border-[var(--color-ink-100)] dark:border-white/10">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center justify-between gap-3 rounded-xl px-3 py-2.5 text-left transition-colors hover:bg-[var(--color-ink-100)]/40 dark:hover:bg-white/[0.04]"
        aria-expanded={open}
      >
        <div className="flex items-center gap-2">
          {open ? (
            <ChevronDown
              className="h-3.5 w-3.5 text-[var(--color-ink-500)]"
              strokeWidth={2.4}
            />
          ) : (
            <ChevronRight
              className="h-3.5 w-3.5 text-[var(--color-ink-500)]"
              strokeWidth={2.4}
            />
          )}
          <span className="text-[12px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-700)]">
            {label}
          </span>
        </div>
        <span className="font-mono text-[10.5px] tabular-nums text-[var(--color-ink-400)]">
          {sizeLabel}
        </span>
      </button>

      {open && (
        <div className="relative border-t border-[var(--color-ink-100)] dark:border-white/10">
          {payload ? (
            <>
              <button
                type="button"
                onClick={handleCopy}
                className="absolute right-2 top-2 z-10 inline-flex items-center gap-1 rounded-md bg-white/80 px-2 py-1 text-[10.5px] font-semibold text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white dark:bg-black/40 dark:text-[var(--color-ink-500)] dark:hover:bg-black/60"
              >
                {copied ? (
                  <>
                    <Check className="h-3 w-3" strokeWidth={2.4} /> Copied
                  </>
                ) : (
                  <>
                    <Copy className="h-3 w-3" strokeWidth={2.4} /> Copy
                  </>
                )}
              </button>
              <pre
                className={cn(
                  "max-h-[420px] overflow-auto px-3 py-3 font-mono text-[10.5px] leading-relaxed text-[var(--color-ink-700)]",
                  "bg-[var(--color-ink-100)]/30 dark:bg-white/[0.03]",
                )}
              >
                {formatted}
              </pre>
            </>
          ) : (
            <div className="px-3 py-3 text-[11.5px] text-[var(--color-ink-400)]">
              No data captured.
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function useFormattedJson(raw: string | null): string {
  if (!raw) return "";
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
