"use client";

// Searchable picker for an integer vendor id (RIOT3 mapId / stationId).
// The caller owns the data fetch (`useMapVendorOptions` /
// `useStationVendorOptions` from `lib/api/facility`) so multiple
// comboboxes on one page share a single fetch.
//
// Value contract: in/out are strings so the surrounding form schema —
// which already represents these IDs as text inputs — doesn't change.
// We accept the string and match against `String(option.vendorId)`.

import { AlertTriangle, ChevronDown, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";

export type IntIdOption = {
  vendorId: number;
  // Primary text — usually the human-readable name.
  label: string;
  // Optional secondary text (e.g. station code, map version).
  secondary?: string | null;
  // Optional small pill on the right (e.g. station type).
  badge?: string | null;
};

type Props = {
  value: string;
  onChange: (next: string) => void;
  options: IntIdOption[];
  loading?: boolean;
  error?: string | null;
  placeholder?: string;
  loadingPlaceholder?: string;
  emptyLabel?: string;
  notInListLabel?: (value: string) => string;
  icon?: React.ReactNode;
  disabled?: boolean;
  className?: string;
};

export function IntIdCombobox({
  value,
  onChange,
  options,
  loading = false,
  error = null,
  placeholder = "Select…",
  loadingPlaceholder = "Loading…",
  emptyLabel = "No matches",
  notInListLabel,
  icon,
  disabled,
  className,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!wrapperRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((o) => {
      if (String(o.vendorId).includes(q)) return true;
      if (o.label.toLowerCase().includes(q)) return true;
      if (o.secondary && o.secondary.toLowerCase().includes(q)) return true;
      return false;
    });
  }, [query, options]);

  useEffect(() => {
    setHighlight(0);
  }, [query, open]);

  useEffect(() => {
    if (!open || !listRef.current) return;
    const el = listRef.current.querySelector(
      `[data-idx="${highlight}"]`,
    ) as HTMLElement | null;
    el?.scrollIntoView({ block: "nearest" });
  }, [highlight, open]);

  function commit(next: string) {
    onChange(next);
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
        commit(String(filtered[highlight].vendorId));
      }
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  // Resolve the currently-selected option (if any) — drives the
  // "ID — Name" rendering when the field isn't being edited.
  const selected = useMemo(
    () => (value ? options.find((o) => String(o.vendorId) === value) : null),
    [value, options],
  );
  const displayValue = open
    ? query
    : selected
      ? `${selected.vendorId} — ${selected.label}`
      : value;

  const isDisabled = disabled || loading || (!!error && options.length === 0);
  const notInList =
    !!value && !loading && !error && options.length > 0 && !selected;

  return (
    <div ref={wrapperRef} className={cn("relative", className)}>
      <div className="relative">
        {icon && (
          <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-ink-400)]">
            {icon}
          </span>
        )}
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
            loading ? loadingPlaceholder : error ? "Failed to load" : placeholder
          }
          autoComplete="off"
          className={cn(
            "w-full rounded-[var(--radius-sm)] border border-white/70 bg-white/65 py-1.5 pr-8 font-mono text-[12px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] backdrop-blur transition-colors",
            "focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20",
            "disabled:cursor-not-allowed disabled:opacity-60",
            "dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]",
            icon ? "pl-7" : "pl-2.5",
          )}
        />
        {value && !loading ? (
          <button
            type="button"
            onMouseDown={(e) => {
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
              {query.trim() ? `${emptyLabel} "${query.trim()}"` : emptyLabel}
            </li>
          ) : (
            filtered.map((o, i) => (
              <li
                key={o.vendorId}
                data-idx={i}
                role="option"
                aria-selected={i === highlight}
                onMouseDown={(e) => {
                  e.preventDefault();
                  commit(String(o.vendorId));
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
                  <span className="font-mono text-[12px]">
                    <span className="font-bold">{o.vendorId}</span>
                    <span className="mx-1.5 text-[var(--color-ink-400)]">—</span>
                    <span className="font-semibold">{o.label}</span>
                  </span>
                  {o.badge && (
                    <span className="rounded-full bg-white/65 px-1.5 py-0 font-mono text-[9.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.06]">
                      {o.badge}
                    </span>
                  )}
                </span>
                {o.secondary && (
                  <span className="truncate text-[11px] text-[var(--color-ink-500)]">
                    {o.secondary}
                  </span>
                )}
              </li>
            ))
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
          {notInListLabel ? notInListLabel(value) : `"${value}" isn't in the list`}
        </p>
      )}
    </div>
  );
}
