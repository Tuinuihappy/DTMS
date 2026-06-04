"use client";

import { Check, Send, Trash2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { cn } from "@/lib/utils";

export function BulkActionBar({
  count,
  onClear,
  onSubmitAll,
  onConfirmAll,
  onDeleteAll,
  busy,
}: {
  count: number;
  onClear: () => void;
  onSubmitAll: () => void;
  onConfirmAll: () => void;
  onDeleteAll: () => void;
  busy: boolean;
}) {
  return (
    <AnimatePresence>
      {count > 0 && (
        <motion.div
          initial={{ opacity: 0, y: 28, scale: 0.96 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: 28, scale: 0.96, transition: { duration: 0.18 } }}
          transition={{ type: "spring", stiffness: 360, damping: 30 }}
          className={cn(
            "fixed inset-x-4 bottom-6 z-30 mx-auto flex max-w-2xl items-center gap-3 rounded-full px-4 py-3",
            "glass-pill",
            "lg:left-[calc(var(--rail-width,80px)+1.5rem)] lg:right-6 lg:mx-0",
          )}
        >
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-white/15 font-mono text-[12px] font-bold text-white">
              {count}
            </span>
            <span className="text-[12.5px] font-semibold text-white truncate">
              {count} order{count === 1 ? "" : "s"} selected
            </span>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <BulkBtn onClick={onSubmitAll} disabled={busy}>
              <Send className="h-3.5 w-3.5" strokeWidth={2.4} />
              <span className="hidden sm:inline">Submit</span>
            </BulkBtn>
            <BulkBtn onClick={onConfirmAll} disabled={busy} tone="success">
              <Check className="h-3.5 w-3.5" strokeWidth={2.4} />
              <span className="hidden sm:inline">Confirm</span>
            </BulkBtn>
            <BulkBtn onClick={onDeleteAll} disabled={busy} tone="coral">
              <Trash2 className="h-3.5 w-3.5" strokeWidth={2.4} />
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
  tone = "default",
  children,
}: {
  onClick: () => void;
  disabled?: boolean;
  tone?: "default" | "success" | "coral";
  children: React.ReactNode;
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      disabled={disabled}
      whileHover={!disabled ? { y: -1 } : {}}
      whileTap={!disabled ? { scale: 0.96 } : {}}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[11.5px] font-semibold transition-all",
        tone === "success"
          ? "bg-[var(--color-success)] text-white hover:shadow-[0_10px_28px_-12px_rgba(16,185,129,0.6)]"
          : tone === "coral"
            ? "bg-[var(--color-coral)] text-white hover:shadow-[0_10px_28px_-12px_rgba(255,107,91,0.6)]"
            : "bg-white/15 text-white hover:bg-white/25",
        "disabled:opacity-50 disabled:cursor-not-allowed",
      )}
    >
      {children}
    </motion.button>
  );
}
