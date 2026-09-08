"use client";

// Unit-of-measure picker. Same panel, keyboard model and portal as
// StationCombobox — but the unit vocabulary is open, so unlike every other
// combobox in the app this one COMMITS FREE TEXT.
//
// That difference drives the state shape: the other comboboxes keep typed
// text in a private `query` and only call onChange when an option is picked,
// so anything unmatched is discarded on blur. Here the input is bound
// straight to `value`, which means every keystroke is already the committed
// value and the suggestion list is only a shortcut. Nothing can be typed and
// then silently lost.

import { ChevronDown } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { ComboboxListPortal } from "@/components/primitives/combobox-list-portal";
import { cn, normalizeSearchText } from "@/lib/utils";

type Props = {
  value: string;
  onChange: (next: string) => void;
  suggestions: readonly string[];
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  inputClassName?: string;
};

export function UomCombobox({
  value,
  onChange,
  suggestions,
  placeholder = "EA",
  disabled,
  className,
  inputClassName,
}: Props) {
  const [open, setOpen] = useState(false);
  const [highlight, setHighlight] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node;
      // The list lives in a body portal, so it is NOT inside wrapperRef —
      // treat clicks in either region as "inside".
      if (!wrapperRef.current?.contains(t) && !listRef.current?.contains(t))
        setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  // Filter on the live value. An exact match still lists everything so the
  // panel doesn't collapse to one row the moment a suggestion is picked.
  const filtered = useMemo(() => {
    const q = normalizeSearchText(value);
    if (!q || suggestions.some((s) => normalizeSearchText(s) === q))
      return suggestions;
    return suggestions.filter((s) => normalizeSearchText(s).includes(q));
  }, [value, suggestions]);

  useEffect(() => {
    setHighlight(0);
  }, [value, open]);

  useEffect(() => {
    if (!open || !listRef.current) return;
    const el = listRef.current.querySelector(
      `[data-idx="${highlight}"]`,
    ) as HTMLElement | null;
    el?.scrollIntoView({ block: "nearest" });
  }, [highlight, open]);

  function pick(unit: string) {
    onChange(unit);
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
      // Enter on a highlighted row picks it; otherwise the typed text is
      // already the value, so just close instead of swallowing it.
      e.preventDefault();
      if (open && filtered[highlight]) pick(filtered[highlight]);
      else setOpen(false);
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  const trimmed = value.trim();
  const isCustom =
    trimmed !== "" && !suggestions.some((s) => s === value);

  return (
    <div ref={wrapperRef} className={cn("relative", className)}>
      <div className="relative">
        <input
          ref={inputRef}
          type="text"
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
            if (!open) setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          disabled={disabled}
          placeholder={placeholder}
          autoComplete="off"
          role="combobox"
          aria-expanded={open}
          aria-autocomplete="list"
          className={cn(
            // No font-mono here (unlike StationCombobox): a unit can be Thai
            // or any other script, and no monospace stack carries those
            // glyphs, so the text would render in a mismatched fallback.
            // The suggestion rows keep mono — they are always latin codes.
            "w-full rounded-[var(--radius-sm)] border border-white/70 bg-white/65 py-2 pl-3 pr-9 text-[12.5px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] backdrop-blur transition-colors",
            "focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20",
            "disabled:cursor-not-allowed disabled:opacity-60",
            "dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]",
            inputClassName,
          )}
        />
        <ChevronDown
          className={cn(
            "pointer-events-none absolute right-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)] transition-transform",
            open && "rotate-180",
          )}
        />
      </div>

      <ComboboxListPortal open={open} anchorRef={wrapperRef}>
        <ul
          ref={listRef}
          role="listbox"
          className="max-h-56 overflow-y-auto rounded-[var(--radius-sm)] border border-white/80 bg-[var(--color-popover)]/95 shadow-[0_18px_42px_-14px_rgba(15,23,42,0.3)] backdrop-blur-md border-[var(--color-ink-100)]"
        >
          {filtered.map((s, i) => (
            <li
              key={s}
              data-idx={i}
              role="option"
              aria-selected={i === highlight}
              onMouseDown={(e) => {
                e.preventDefault();
                pick(s);
              }}
              onMouseEnter={() => setHighlight(i)}
              className={cn(
                "flex cursor-pointer items-center justify-between gap-2 px-3 py-1.5 transition-colors",
                i === highlight
                  ? "bg-[var(--color-brand-500)]/15 text-[var(--color-ink-900)] dark:text-white"
                  : "text-[var(--color-ink-700)] dark:text-[var(--color-ink-200)]",
              )}
            >
              <span className="font-mono text-[12.5px] font-semibold">{s}</span>
              {s === value && (
                <span className="rounded-full bg-white/65 px-1.5 py-0 font-mono text-[9.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.06]">
                  current
                </span>
              )}
            </li>
          ))}
          {isCustom && (
            // Not an option to click — the typed text is already the value.
            // This row exists so the panel never looks like a dead end when
            // someone names a unit the shortcut list doesn't carry.
            <li className="border-t border-[var(--color-ink-100)] px-3 py-1.5 text-[11px] italic text-[var(--color-ink-500)] dark:border-white/[0.06]">
              Using “{trimmed}” as typed
            </li>
          )}
        </ul>
      </ComboboxListPortal>

      {isCustom && !open && (
        <p className="mt-1 text-[10.5px] text-[var(--color-amber)]">
          “{trimmed}” isn’t one of the usual units
        </p>
      )}
    </div>
  );
}
