"use client";

// Searchable warehouse picker — the building/site-level picker added
// in Phase 2.7b (per ADR-002 + Phase 2.1-2.7a backend). Mirrors
// station-combobox shape so the existing keyboard / search / clear /
// open-on-focus interactions stay consistent across pickers.
//
// Mockup reference: docs/multi-mode-transport/diagrams/ui-mockups.md
// (Warehouse → AmrStation 2-Step Picker, States A-E)
//
// Differences from StationCombobox:
//   - Displays per-warehouse service-mode chips (Amr / Manual / Fleet)
//     so operators see at a glance which modes a warehouse serves
//   - Uses Building icon instead of MapPin (visually distinguishes
//     warehouse picker from station picker when both are stacked)
//   - The caller owns the data fetch via useWarehouseOptions; multiple
//     pickers on one page share a single fetch (e.g. pickup + drop
//     warehouse on the order create form)

import { AlertTriangle, Building2, ChevronDown, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type {
  TransportModeWire,
  WarehouseOption,
} from "@/lib/api/warehouses";
import { cn } from "@/lib/utils";

type Props = {
  value: string;   // warehouse code (e.g. "WH-BKK-01")
  onChange: (next: string) => void;
  warehouses: WarehouseOption[];
  loading?: boolean;
  error?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  // Optional client-side filter — only show warehouses that serve this
  // mode. (Server-side filter is preferred — pass serviceMode to
  // useWarehouseOptions. This client filter is for cases where the
  // hook fetches the unfiltered list and one picker needs a narrower
  // subset.)
  filterByServiceMode?: TransportModeWire;
};

export function WarehouseCombobox({
  value,
  onChange,
  warehouses,
  loading = false,
  error = null,
  placeholder = "Search warehouse…",
  disabled,
  className,
  filterByServiceMode,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  // Click-outside to close. Effect re-installs when open flips so the
  // listener isn't running when the picker is hidden — keeps the global
  // mousedown handler count low when many pickers exist.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!wrapperRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const filtered = useMemo(() => {
    let list = warehouses;
    if (filterByServiceMode) {
      list = list.filter((w) => w.serviceModes.includes(filterByServiceMode));
    }
    const q = query.trim().toLowerCase();
    if (!q) return list;
    return list.filter(
      (w) =>
        w.code.toLowerCase().includes(q) ||
        w.name.toLowerCase().includes(q),
    );
  }, [query, warehouses, filterByServiceMode]);

  // Reset highlight on filter change so arrow keys don't land on the
  // "wrong" row after the list shrinks.
  useEffect(() => {
    setHighlight(0);
  }, [query, open, filterByServiceMode]);

  // Scroll highlighted row into view on keyboard navigation.
  useEffect(() => {
    if (!open || !listRef.current) return;
    const el = listRef.current.querySelector(
      `[data-idx="${highlight}"]`,
    ) as HTMLElement | null;
    el?.scrollIntoView({ block: "nearest" });
  }, [highlight, open]);

  function commit(code: string) {
    onChange(code);
    setQuery("");
    setOpen(false);
    inputRef.current?.blur();
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      if (!open) setOpen(true);
      setHighlight((h) => Math.min(Math.max(filtered.length - 1, 0), h + 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(0, h - 1));
    } else if (e.key === "Enter") {
      if (open && filtered[highlight]) {
        e.preventDefault();
        commit(filtered[highlight].code);
      }
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  const displayValue = open ? query : value;
  const isDisabled = disabled || loading || (!!error && warehouses.length === 0);
  // "Selected code isn't in the active list" — warns the operator that
  // the saved selection is stale (warehouse was deactivated, code was
  // changed, etc.). The picker doesn't auto-clear the value because
  // the saved selection still has meaning for audit.
  const notInList =
    !!value && warehouses.length > 0 && !warehouses.some((w) => w.code === value);

  // Filtered-mode hint — when filterByServiceMode hides warehouses,
  // surface the count so operators don't wonder "where are my AMR
  // warehouses?" when really they were filtered out.
  const hiddenCount =
    filterByServiceMode && !query.trim()
      ? warehouses.length - filtered.length
      : 0;

  return (
    <div ref={wrapperRef} className={cn("relative", className)}>
      <div className="relative">
        <Building2
          className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)]"
          strokeWidth={2.2}
        />
        <input
          ref={inputRef}
          type="text"
          value={displayValue}
          onChange={(e) => {
            setQuery(e.target.value);
            if (!open) setOpen(true);
          }}
          onFocus={() => {
            setQuery("");
            setOpen(true);
          }}
          onKeyDown={onKeyDown}
          disabled={isDisabled}
          placeholder={
            loading
              ? "Loading warehouses…"
              : error
                ? "Failed to load"
                : placeholder
          }
          autoComplete="off"
          className={cn(
            "w-full rounded-[var(--radius-sm)] border border-white/70 bg-white/65 py-2 pl-9 pr-9 font-mono text-[12.5px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] backdrop-blur transition-colors",
            "focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20",
            "disabled:cursor-not-allowed disabled:opacity-60",
            "dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]",
          )}
        />
        {value && !loading ? (
          <button
            type="button"
            onMouseDown={(e) => {
              // Prevent input losing focus before the clear lands —
              // otherwise the focus flip re-opens the dropdown and the
              // operator has to dismiss it manually.
              e.preventDefault();
              commit("");
            }}
            className="absolute right-2 top-1/2 grid h-5 w-5 -translate-y-1/2 cursor-pointer place-items-center rounded-full text-[var(--color-ink-400)] transition-colors hover:bg-white/55 hover:text-[var(--color-ink-700)] dark:hover:bg-white/[0.08]"
            aria-label="Clear selection"
          >
            <X className="h-3 w-3" strokeWidth={2.4} />
          </button>
        ) : (
          <ChevronDown
            className={cn(
              "pointer-events-none absolute right-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)] transition-transform",
              open && "rotate-180",
            )}
          />
        )}
      </div>

      {open && !loading && !error && (
        <ul
          ref={listRef}
          role="listbox"
          className="absolute left-0 right-0 z-30 mt-1 max-h-56 overflow-y-auto rounded-[var(--radius-sm)] border border-white/80 bg-[var(--color-popover)]/95 shadow-[0_18px_42px_-14px_rgba(15,23,42,0.3)] backdrop-blur-md border-[var(--color-ink-100)]"
        >
          {filtered.length === 0 ? (
            <li className="px-3 py-2 text-[12px] italic text-[var(--color-ink-400)]">
              {query.trim()
                ? `No warehouse matches "${query.trim()}"`
                : filterByServiceMode
                  ? `No warehouses serve ${filterByServiceMode} mode`
                  : "No active warehouses available"}
            </li>
          ) : (
            filtered.map((w, i) => (
              <li
                key={w.id}
                data-idx={i}
                role="option"
                aria-selected={i === highlight}
                onMouseDown={(e) => {
                  e.preventDefault();
                  commit(w.code);
                }}
                onMouseEnter={() => setHighlight(i)}
                className={cn(
                  "flex cursor-pointer flex-col gap-0.5 px-3 py-1.5 transition-colors",
                  i === highlight
                    ? "bg-[var(--color-brand-500)]/15 text-[var(--color-ink-900)] dark:text-white"
                    : "text-[var(--color-ink-700)] dark:text-[var(--color-ink-200)]",
                )}
              >
                <span className="flex items-center justify-between gap-2">
                  <span className="font-mono text-[12.5px] font-semibold">
                    {w.code}
                  </span>
                  <span className="flex items-center gap-1">
                    {w.serviceModes.map((mode) => (
                      <span
                        key={mode}
                        className="rounded-full bg-white/65 px-1.5 py-0 font-mono text-[9.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.06]"
                      >
                        {mode}
                      </span>
                    ))}
                  </span>
                </span>
                <span className="truncate text-[11px] text-[var(--color-ink-500)]">
                  {w.name}
                </span>
              </li>
            ))
          )}

          {hiddenCount > 0 && (
            <li className="border-t border-[var(--color-ink-100)] px-3 py-1.5 text-[10.5px] italic text-[var(--color-ink-400)]">
              {hiddenCount} warehouse{hiddenCount === 1 ? "" : "s"} hidden
              (don&apos;t serve {filterByServiceMode} mode)
            </li>
          )}
        </ul>
      )}

      {error && (
        <p className="mt-1 inline-flex items-center gap-1 text-[10.5px] text-[var(--color-coral)]">
          <AlertTriangle className="h-3 w-3" strokeWidth={2.3} />
          {error}
        </p>
      )}
      {notInList && !open && (
        <p className="mt-1 text-[10.5px] text-[var(--color-amber)]">
          &ldquo;{value}&rdquo; isn&apos;t in the active warehouse list
        </p>
      )}
    </div>
  );
}
