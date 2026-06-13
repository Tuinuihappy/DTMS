"use client";

import { Bookmark, BookmarkPlus, Check, Trash2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

const STORAGE_KEY = "orders:saved-filters";

export type FilterSnapshot = Record<string, unknown>;

export type SavedFilter = {
  id: string;
  name: string;
  snapshot: FilterSnapshot;
};

export function loadSavedFilters(): SavedFilter[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as SavedFilter[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function persist(filters: SavedFilter[]) {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(filters));
}

export function SavedFiltersMenu({
  currentSnapshot,
  onApply,
}: {
  currentSnapshot: FilterSnapshot;
  onApply: (snap: FilterSnapshot) => void;
}) {
  const [open, setOpen] = useState(false);
  const [naming, setNaming] = useState(false);
  const [draftName, setDraftName] = useState("");
  const [filters, setFilters] = useState<SavedFilter[]>([]);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setFilters(loadSavedFilters());
  }, []);

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) {
        setOpen(false);
        setNaming(false);
      }
    };
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, [open]);

  const save = () => {
    const name = draftName.trim();
    if (!name) return;
    const next: SavedFilter[] = [
      ...filters,
      { id: crypto.randomUUID(), name, snapshot: currentSnapshot },
    ];
    setFilters(next);
    persist(next);
    setDraftName("");
    setNaming(false);
  };

  const remove = (id: string) => {
    const next = filters.filter((f) => f.id !== id);
    setFilters(next);
    persist(next);
  };

  return (
    <div ref={rootRef} className="relative">
      <motion.button
        type="button"
        onClick={() => setOpen((v) => !v)}
        title="Saved filters"
        whileHover={{ y: -1 }}
        whileTap={{ scale: 0.97 }}
        className={cn(
          "inline-flex items-center gap-1.5 rounded-full px-3 py-2 text-[12px] font-semibold",
          "bg-white/60 text-[var(--color-ink-700)] border border-white/70",
          "transition-all hover:bg-white/90",
          "dark:bg-white/[0.05] dark:text-[var(--color-ink-700)] dark:border-white/10 dark:hover:bg-white/[0.1]",
        )}
      >
        <Bookmark className="h-3.5 w-3.5" strokeWidth={2.4} />
        <span className="hidden sm:inline">Saved</span>
        {filters.length > 0 && (
          <span className="rounded-full bg-[var(--color-brand-900)]/10 px-1.5 text-[10px] font-mono tabular-nums text-[var(--color-brand-900)] dark:bg-[var(--color-brand-500)]/20 dark:text-[var(--color-brand-500)]">
            {filters.length}
          </span>
        )}
      </motion.button>

      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -4 }}
            transition={{ duration: 0.15 }}
            className={cn(
              "absolute right-0 z-30 mt-2 w-72 overflow-hidden rounded-[var(--radius-lg)]",
              "bg-white/95 backdrop-blur-xl border border-white/70 shadow-[0_18px_44px_-18px_rgba(15,23,42,0.45)]",
              "dark:bg-[var(--color-ink-950)]/95 dark:border-white/10",
            )}
          >
            <div className="px-3 pt-3 pb-2 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
              Saved filters
            </div>
            {filters.length === 0 && !naming && (
              <div className="px-3 pb-3 text-[12px] text-[var(--color-ink-500)]">
                No saved filters yet.
              </div>
            )}
            <div className="max-h-60 overflow-y-auto">
              {filters.map((f) => (
                <div
                  key={f.id}
                  className="group flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-white/60 dark:hover:bg-white/[0.05]"
                >
                  <button
                    type="button"
                    onClick={() => {
                      onApply(f.snapshot);
                      setOpen(false);
                    }}
                    className="flex flex-1 items-center gap-2 text-left text-[12.5px] font-medium text-[var(--color-ink-800)] dark:text-[var(--color-ink-100)]"
                  >
                    <Check
                      className="h-3.5 w-3.5 text-[var(--color-brand-500)]"
                      strokeWidth={2.4}
                    />
                    <span className="truncate">{f.name}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => remove(f.id)}
                    title="Delete"
                    className="opacity-0 transition-opacity group-hover:opacity-100"
                  >
                    <Trash2
                      className="h-3.5 w-3.5 text-[var(--color-coral-500)]"
                      strokeWidth={2.4}
                    />
                  </button>
                </div>
              ))}
            </div>
            <div className="border-t border-white/60 dark:border-white/10 p-2">
              {naming ? (
                <div className="flex items-center gap-1.5">
                  <input
                    autoFocus
                    type="text"
                    value={draftName}
                    onChange={(e) => setDraftName(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") save();
                      if (e.key === "Escape") setNaming(false);
                    }}
                    placeholder="Name this filter…"
                    className={cn(
                      "flex-1 rounded-md bg-white/60 px-2 py-1.5 text-[12px]",
                      "border border-white/70 text-[var(--color-ink-900)] backdrop-blur-md",
                      "focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40",
                      "dark:bg-white/[0.05] dark:border-white/10",
                    )}
                  />
                  <button
                    type="button"
                    onClick={save}
                    disabled={!draftName.trim()}
                    className="grid h-7 w-7 place-items-center rounded-md bg-[var(--color-brand-900)] text-white disabled:opacity-40 dark:bg-[var(--color-brand-500)]"
                  >
                    <Check className="h-3.5 w-3.5" strokeWidth={2.6} />
                  </button>
                  <button
                    type="button"
                    onClick={() => setNaming(false)}
                    className="grid h-7 w-7 place-items-center rounded-md text-[var(--color-ink-500)] hover:bg-white/60 dark:hover:bg-white/[0.05]"
                  >
                    <X className="h-3.5 w-3.5" strokeWidth={2.4} />
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  onClick={() => setNaming(true)}
                  className={cn(
                    "flex w-full items-center gap-1.5 rounded-md px-2 py-1.5 text-[12px] font-semibold",
                    "text-[var(--color-brand-900)] hover:bg-[var(--color-brand-900)]/5",
                    "dark:text-[var(--color-brand-500)] dark:hover:bg-[var(--color-brand-500)]/10",
                  )}
                >
                  <BookmarkPlus className="h-3.5 w-3.5" strokeWidth={2.4} />
                  Save current filters
                </button>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
