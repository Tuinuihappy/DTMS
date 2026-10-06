"use client";

// WMS PR-4 — searchable WMS location picker for Manual/Fleet orders.
// Mirrors WarehouseCombobox / StationCombobox shape (same keyboard nav,
// click-outside, clear button, open-on-focus). Key differences:
//   • Filters a whole-catalogue `catalog` (useWmsLocationCatalog, loaded
//     once by the host) in the browser — every location is listed and
//     typing filters instantly. Without a complete catalog (none passed,
//     load failed, or more rows than one page) it falls back to
//     debounced server-side search via useWmsLocationSearch.
//   • Shows the parent-zone display name as a subtle chip so operators
//     see "STF_35 · WIP" at a glance.
//   • Uses MapPin icon (physical location, matches WMS domain semantics).

import { AlertTriangle, ChevronDown, Loader2, MapPin, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  getWmsLocationByCode,
  useWmsLocationSearch,
  type WmsLocationCatalog,
  type WmsLocationSummaryDto,
} from "@/lib/api/wms-locations";
import { ComboboxListPortal } from "@/components/primitives/combobox-list-portal";
import { cn } from "@/lib/utils";

type Props = {
  value: string;                       // location code (e.g. "STF_35")
  onChange: (next: string) => void;
  catalog?: WmsLocationCatalog;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
};

export function WmsLocationCombobox({
  value,
  onChange,
  catalog,
  placeholder = "Search WMS location…",
  disabled,
  className,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  // Autofill guard — see StationCombobox: readOnly until focused so
  // Chrome autofill can't desync the DOM text from the search query.
  const [autofillGuard, setAutofillGuard] = useState(true);
  const [selectedDetail, setSelectedDetail] =
    useState<WmsLocationSummaryDto | null>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  // Local mode: the catalog holds every active location, so filter it here.
  const localMode = !!catalog && catalog.complete && !catalog.error;
  const catalogLoading = !!catalog && catalog.loading;
  const server = useWmsLocationSearch(query, {
    pageSize: 20,
    debounceMs: 300,
    enabled: open && !localMode && !catalogLoading,
  });
  // Same match as the server search: code or display name contains the
  // text, case-insensitive; the catalog is already ordered by code.
  const localItems = useMemo(() => {
    if (!localMode || !catalog) return [];
    const q = query.trim().toLowerCase();
    if (!q) return catalog.locations;
    return catalog.locations.filter(
      (loc) =>
        loc.code.toLowerCase().includes(q) ||
        loc.displayName.toLowerCase().includes(q),
    );
  }, [localMode, catalog, query]);
  const items = localMode ? localItems : server.items;
  const loading = catalogLoading || (!localMode && server.loading);
  const error = localMode ? null : server.error;
  // Items answer the text in the box (not a previous keystroke's search).
  // Always true when filtering locally.
  const itemsFresh =
    localMode || (!catalogLoading && !server.loading && server.resolvedQuery === query);
  // Server mode lists at most one page — say so when there's more.
  const hiddenCount = localMode ? 0 : Math.max(server.total - server.items.length, 0);
  // Enter pressed before itemsFresh — resolved by the effect below once
  // the search for the current text lands.
  const [pendingEnter, setPendingEnter] = useState(false);

  // Fetch detail for the initially-set value so the picker can render a
  // "STF_35 · WIP" chip immediately when opening an existing order for
  // edit. Skips if the user has already typed something (open state).
  useEffect(() => {
    let cancelled = false;
    if (!value.trim()) {
      setSelectedDetail(null);
      return;
    }
    // Only refetch when the external value changes AND we don't already
    // have that code loaded.
    if (selectedDetail?.code.toLowerCase() === value.toLowerCase()) return;
    // The catalog usually has it — no request needed.
    const known = catalog?.locations.find(
      (loc) => loc.code.toLowerCase() === value.toLowerCase(),
    );
    if (known) {
      setSelectedDetail(known);
      return;
    }
    if (catalog?.loading) return; // re-runs when the catalog lands
    getWmsLocationByCode(value).then((loc) => {
      if (!cancelled) setSelectedDetail(loc);
    });
    return () => {
      cancelled = true;
    };
    // selectedDetail is intentionally excluded — including it would loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, catalog?.locations, catalog?.loading]);

  // Click-outside to close.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node;
      // Panel lives in a body portal — clicks there count as "inside".
      if (!wrapperRef.current?.contains(t) && !panelRef.current?.contains(t))
        setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  // Reset highlight on result-set change.
  useEffect(() => {
    setHighlight(0);
  }, [items, open]);

  // Scroll highlighted row into view on keyboard navigation.
  useEffect(() => {
    if (!open || !listRef.current) return;
    const el = listRef.current.querySelector(
      `[data-idx="${highlight}"]`,
    ) as HTMLElement | null;
    el?.scrollIntoView({ block: "nearest" });
  }, [highlight, open]);

  const commit = (loc: WmsLocationSummaryDto) => {
    onChange(loc.code);
    setSelectedDetail(loc);
    setQuery("");
    setOpen(false);
    inputRef.current?.blur();
  };

  // A pending Enter commits only an exact code match for what was typed.
  // Anything else leaves the list open for the user to choose — never
  // auto-pick a near miss (typing STF_3 must not land on STF_30).
  useEffect(() => {
    if (!pendingEnter) return;
    if (!open || error) {
      setPendingEnter(false);
      return;
    }
    if (!itemsFresh) return;
    setPendingEnter(false);
    const typed = query.trim().toLowerCase();
    const exact = items.find((loc) => loc.code.toLowerCase() === typed);
    if (exact) commit(exact);
    // commit is a fresh closure each render; the pendingEnter guard keeps
    // this from re-running for it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingEnter, itemsFresh, items, query, open, error]);

  // Esc on the open list (via the layer stack): close it and leave the field.
  const dismissList = () => {
    setOpen(false);
    inputRef.current?.blur();
  };

  const clear = () => {
    onChange("");
    setSelectedDetail(null);
    setQuery("");
    setOpen(true);
    inputRef.current?.focus();
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open) {
      if (e.key === "ArrowDown" || e.key === "Enter") {
        setOpen(true);
        e.preventDefault();
      }
      return;
    }
    if (e.key === "ArrowDown") {
      setHighlight((h) => Math.min(h + 1, Math.max(items.length - 1, 0)));
      e.preventDefault();
    } else if (e.key === "ArrowUp") {
      setHighlight((h) => Math.max(h - 1, 0));
      e.preventDefault();
    } else if (e.key === "Enter") {
      // Before the search for the current text lands, items[highlight] is
      // a row from the previous query — defer instead of picking it.
      if (itemsFresh) {
        const pick = items[highlight];
        if (pick) commit(pick);
      } else {
        setPendingEnter(true);
      }
      e.preventDefault();
    }
    // Esc is handled by the layer stack (ComboboxListPortal's onDismiss).
  };

  // What to show in the collapsed input: either the current selection
  // (with zone chip) or the raw code the caller passed in (for edit
  // forms that haven't finished loading detail yet).
  const displayCode = value || "";
  const displayZone =
    selectedDetail?.code.toLowerCase() === value.toLowerCase()
      ? selectedDetail?.parentLocationDisplayName ??
        selectedDetail?.parentLocationCode ??
        null
      : null;

  return (
    <div
      ref={wrapperRef}
      className={cn("relative", className)}
      aria-expanded={open}
      aria-haspopup="listbox"
      role="combobox"
    >
      <div
        className={cn(
          "flex h-9 items-center gap-1.5 rounded-md border border-gray-300 bg-white px-2.5 focus-within:border-blue-500 focus-within:ring-1 focus-within:ring-blue-500",
          disabled && "cursor-not-allowed bg-gray-50 opacity-60",
        )}
      >
        <MapPin className="h-3.5 w-3.5 flex-none text-gray-400" />
        <input
          ref={inputRef}
          type="text"
          value={open ? query : displayCode}
          onChange={(e) => {
            setQuery(e.target.value);
            // Typing on after Enter means the user hasn't settled yet.
            setPendingEnter(false);
            if (!open) setOpen(true);
          }}
          onFocus={(e) => {
            e.currentTarget.readOnly = false;
            setAutofillGuard(false);
            setOpen(true);
          }}
          onBlur={() => setAutofillGuard(true)}
          onKeyDown={onKeyDown}
          readOnly={autofillGuard}
          placeholder={placeholder}
          disabled={disabled}
          className="min-w-0 flex-1 border-0 bg-transparent text-sm outline-none placeholder:text-gray-400"
        />
        {displayZone && !open && (
          <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-600">
            {displayZone}
          </span>
        )}
        {value && !disabled && (
          <button
            type="button"
            onClick={clear}
            className="text-gray-400 hover:text-gray-600"
            aria-label="Clear"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
        <ChevronDown
          className={cn(
            "h-3.5 w-3.5 flex-none text-gray-400 transition-transform",
            open && "rotate-180",
          )}
        />
      </div>

      <ComboboxListPortal open={open} anchorRef={wrapperRef} onDismiss={dismissList}>
        <div
          ref={panelRef}
          className="max-h-64 overflow-hidden rounded-md border border-gray-200 bg-white shadow-lg"
        >
          {error && (
            <div className="flex items-center gap-1.5 border-b border-gray-100 bg-red-50 px-2.5 py-1.5 text-xs text-red-700">
              <AlertTriangle className="h-3 w-3" />
              {error}
            </div>
          )}
          <ul
            ref={listRef}
            role="listbox"
            className="max-h-64 overflow-y-auto py-1"
          >
            {loading && (
              <li className="flex items-center gap-1.5 px-2.5 py-2 text-xs text-gray-500">
                <Loader2 className="h-3 w-3 animate-spin" />
                {catalogLoading ? "Loading locations…" : "Searching…"}
              </li>
            )}
            {!loading && items.length === 0 && (
              <li className="px-2.5 py-2 text-xs text-gray-500">
                {query.trim()
                  ? `No locations match "${query.trim()}"`
                  : "Type to search WMS locations…"}
              </li>
            )}
            {items.map((loc, idx) => {
              const isSelected = loc.code.toLowerCase() === value.toLowerCase();
              const isHighlighted = idx === highlight;
              return (
                <li
                  key={loc.id}
                  data-idx={idx}
                  role="option"
                  aria-selected={isSelected}
                  onMouseEnter={() => setHighlight(idx)}
                  onMouseDown={(e) => {
                    // preventDefault so input blur doesn't fire and close the popup
                    e.preventDefault();
                    commit(loc);
                  }}
                  className={cn(
                    "flex cursor-pointer items-center gap-2 px-2.5 py-1.5 text-xs",
                    isHighlighted && "bg-blue-50",
                    isSelected && "font-medium",
                    !loc.isActive && "text-gray-400 italic",
                  )}
                >
                  <MapPin className="h-3 w-3 flex-none text-gray-400" />
                  <span className="flex-1 truncate">
                    <span className="font-mono">{loc.code}</span>
                    {loc.displayName && loc.displayName !== loc.code && (
                      <span className="ml-1.5 text-gray-500">
                        {loc.displayName}
                      </span>
                    )}
                  </span>
                  {loc.parentLocationDisplayName && (
                    <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-600">
                      {loc.parentLocationDisplayName}
                    </span>
                  )}
                  {!loc.isActive && (
                    <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-medium text-amber-700">
                      inactive
                    </span>
                  )}
                </li>
              );
            })}
            {!loading && hiddenCount > 0 && (
              <li className="px-2.5 py-1.5 text-[11px] text-gray-500">
                Showing {items.length} of {server.total} — keep typing to narrow
              </li>
            )}
          </ul>
        </div>
      </ComboboxListPortal>
    </div>
  );
}
