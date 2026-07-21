"use client";

// Searchable action-template-name picker for ACT missions in the
// order-template editor. The mission's `actionTemplateName` is a free
// string keyed by name (not id), so the value in/out of this combobox
// is the name itself. Caller owns the data fetch via
// `useActionTemplateOptions` from `lib/api/action-templates`.

import { AlertTriangle, ChevronDown, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type { ActionTemplateOption } from "@/lib/api/action-templates";
import { ComboboxListPortal } from "@/components/primitives/combobox-list-portal";
import { cn, normalizeSearchText } from "@/lib/utils";

type Props = {
  value: string;
  onChange: (next: string) => void;
  templates: ActionTemplateOption[];
  loading?: boolean;
  error?: string | null;
  placeholder?: string;
  icon?: React.ReactNode;
  disabled?: boolean;
  className?: string;
};

export function ActionTemplateCombobox({
  value,
  onChange,
  templates,
  loading = false,
  error = null,
  placeholder = "Search action template…",
  icon,
  disabled,
  className,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  // Autofill guard — see StationCombobox: readOnly until focused so
  // Chrome autofill can't desync the DOM text from the filter query.
  const [autofillGuard, setAutofillGuard] = useState(true);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node;
      // List lives in a body portal — clicks there count as "inside".
      if (!wrapperRef.current?.contains(t) && !listRef.current?.contains(t))
        setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const filtered = useMemo(() => {
    const q = normalizeSearchText(query);
    if (!q) return templates;
    return templates.filter(
      (t) =>
        normalizeSearchText(t.name).includes(q) ||
        normalizeSearchText(t.actionType).includes(q),
    );
  }, [query, templates]);

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

  function commit(name: string) {
    onChange(name);
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
        commit(filtered[highlight].name);
      }
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  const displayValue = open ? query : value;
  const isDisabled = disabled || loading || (!!error && templates.length === 0);
  const notInList =
    !!value && !loading && !error && templates.length > 0 && !templates.some((t) => t.name === value);

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
          onFocus={(e) => {
            e.currentTarget.readOnly = false;
            setAutofillGuard(false);
            setQuery("");
            setOpen(true);
          }}
          onBlur={() => setAutofillGuard(true)}
          onKeyDown={onKeyDown}
          readOnly={autofillGuard}
          disabled={isDisabled}
          placeholder={
            loading
              ? "Loading action templates…"
              : error
                ? "Failed to load"
                : placeholder
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

      <ComboboxListPortal open={open && !loading && !error} anchorRef={wrapperRef}>
        <ul
          ref={listRef}
          role="listbox"
          className="max-h-56 overflow-y-auto rounded-[var(--radius-sm)] border border-white/80 bg-[var(--color-popover)]/95 shadow-[0_18px_42px_-14px_rgba(15,23,42,0.3)] backdrop-blur-md border-[var(--color-ink-100)]"
        >
          {filtered.length === 0 ? (
            <li className="px-3 py-2 text-[12px] italic text-[var(--color-ink-400)]">
              {query.trim()
                ? `No template matches "${query.trim()}"`
                : "No active action templates available"}
            </li>
          ) : (
            filtered.map((t, i) => (
              <li
                key={t.name}
                data-idx={i}
                role="option"
                aria-selected={i === highlight}
                onMouseDown={(e) => {
                  e.preventDefault();
                  commit(t.name);
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
                    {t.name}
                  </span>
                  <span className="rounded-full bg-white/65 px-1.5 py-0 font-mono text-[9.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.06]">
                    {t.category}
                  </span>
                </span>
                <span className="truncate text-[11px] text-[var(--color-ink-500)]">
                  {t.actionType}
                </span>
              </li>
            ))
          )}
        </ul>
      </ComboboxListPortal>

      {error && (
        <p className="mt-1 inline-flex items-center gap-1 text-[10.5px] text-[var(--color-coral)]">
          <AlertTriangle className="h-3 w-3" strokeWidth={2.3} />
          {error}
        </p>
      )}
      {notInList && !open && (
        <p className="mt-1 text-[10.5px] text-[var(--color-amber)]">
          “{value}” isn’t in the active template list
        </p>
      )}
    </div>
  );
}
