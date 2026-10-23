"use client";

import { Send, Trash2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { Button } from "@/components/primitives/button";
import { T, spring } from "@/lib/motion";
import { cn } from "@/lib/utils";

// How far the toast stack moves up while the bar is parked at the bottom
// (bar is bottom-6 + ~56px tall; this clears it with a small gap).
const TOAST_LIFT = "72px";

// Phase P5 — the standalone Confirm bulk action was removed. Submit
// auto-confirms atomically on the backend, so "Submit N orders" is the
// only forward-transition bulk button; Cancel is still present because
// cancellation is a different intent that operates on confirmed orders.
export function BulkActionBar({
  count,
  onClear,
  onSubmitAll,
  onDeleteAll,
  busy,
}: {
  count: number;
  onClear: () => void;
  onSubmitAll: () => void;
  onDeleteAll: () => void;
  busy: boolean;
}) {
  const visible = count > 0;
  // `busy` is shared by both actions; remember which one was clicked so
  // only that button spins.
  const [lastAction, setLastAction] = useState<"submit" | "delete" | null>(null);

  // Toasts (e.g. "Submitted 3 orders") would otherwise land on top of the
  // bar. Set on the root so the toast stack, rendered elsewhere, sees it.
  useEffect(() => {
    if (!visible) return;
    const root = document.documentElement;
    root.style.setProperty("--toast-lift", TOAST_LIFT);
    return () => {
      root.style.removeProperty("--toast-lift");
    };
  }, [visible]);

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          key="bulk-bar"
          initial={{ opacity: 0, y: 28, scale: 0.96 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: 28, scale: 0.96, transition: T.exit }}
          transition={spring.dialog}
          aria-busy={busy || undefined}
          className={cn(
            "fixed inset-x-4 bottom-6 z-30 mx-auto flex max-w-2xl items-center gap-3 rounded-full px-4 py-3",
            "glass-pill",
            "lg:left-[calc(var(--rail-width,80px)+1.5rem)] lg:right-6 lg:mx-0",
          )}
        >
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <span className="relative grid h-8 w-8 shrink-0 place-items-center overflow-hidden rounded-full bg-white/15 font-mono text-[12px] font-bold text-white">
              {/* The number slides as the selection grows/shrinks, so a
                  row click visibly lands in the count. */}
              <AnimatePresence mode="popLayout" initial={false}>
                <motion.span
                  key={count}
                  initial={{ y: 10, opacity: 0 }}
                  animate={{ y: 0, opacity: 1 }}
                  exit={{ y: -10, opacity: 0, transition: T.exit }}
                  transition={T.fast}
                >
                  {count}
                </motion.span>
              </AnimatePresence>
            </span>
            <span className="text-[12.5px] font-semibold text-white truncate">
              {count} order{count === 1 ? "" : "s"} selected
            </span>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <BulkBtn
              onClick={() => {
                setLastAction("submit");
                onSubmitAll();
              }}
              disabled={busy}
              pending={busy && lastAction === "submit"}
              tone="success"
              icon={<Send className="h-3.5 w-3.5" strokeWidth={2.4} />}
            >
              <span className="hidden sm:inline">Submit</span>
            </BulkBtn>
            <BulkBtn
              onClick={() => {
                setLastAction("delete");
                onDeleteAll();
              }}
              disabled={busy}
              pending={busy && lastAction === "delete"}
              tone="coral"
              icon={<Trash2 className="h-3.5 w-3.5" strokeWidth={2.4} />}
            >
              <span className="hidden sm:inline">Cancel</span>
            </BulkBtn>
            <button
              type="button"
              onClick={onClear}
              className="rounded-full p-1.5 text-white/70 transition-colors hover:bg-white/10 hover:text-white"
              aria-label="Clear selection"
            >
              <X className="h-3.5 w-3.5" strokeWidth={2.4} />
            </button>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function BulkBtn({
  onClick,
  disabled,
  pending,
  tone = "default",
  icon,
  children,
}: {
  onClick: () => void;
  disabled?: boolean;
  pending?: boolean;
  tone?: "default" | "success" | "coral";
  icon: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <Button
      onClick={onClick}
      disabled={disabled}
      pending={pending}
      icon={icon}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[11.5px] font-semibold",
        tone === "success"
          ? "bg-[var(--color-success)] text-white hover:shadow-[0_10px_28px_-12px_rgba(16,185,129,0.6)]"
          : tone === "coral"
            ? "bg-[var(--color-coral)] text-white hover:shadow-[0_10px_28px_-12px_rgba(255,107,91,0.6)]"
            : "bg-white/15 text-white hover:bg-white/25",
        "disabled:opacity-50 disabled:cursor-not-allowed",
      )}
    >
      {children}
    </Button>
  );
}
