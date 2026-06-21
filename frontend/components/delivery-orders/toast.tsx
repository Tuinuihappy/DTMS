"use client";

import { AlertCircle, CheckCircle2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
} from "react";
import { cn } from "@/lib/utils";

type Toast = {
  id: string;
  tone: "success" | "error" | "info";
  message: string;
  // Inline action — usually undo for destructive ops.
  action?: { label: string; onClick: () => void };
};

type ToastContext = {
  push: (t: Omit<Toast, "id">) => void;
};

const Ctx = createContext<ToastContext | null>(null);

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [items, setItems] = useState<Toast[]>([]);
  const timers = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map());

  const dismiss = useCallback((id: string) => {
    setItems((prev) => prev.filter((x) => x.id !== id));
    const t = timers.current.get(id);
    if (t) {
      clearTimeout(t);
      timers.current.delete(id);
    }
  }, []);

  const push = useCallback(
    (t: Omit<Toast, "id">) => {
      const id = Math.random().toString(36).slice(2, 9);
      setItems((prev) => [...prev, { ...t, id }]);
      // Errors linger 6s, success/info 4s, give the user time to hit "Undo".
      const lifeMs = t.action ? 6500 : t.tone === "error" ? 5500 : 3800;
      timers.current.set(
        id,
        setTimeout(() => dismiss(id), lifeMs),
      );
    },
    [dismiss],
  );

  const ctx = useMemo(() => ({ push }), [push]);

  return (
    <Ctx.Provider value={ctx}>
      {children}
      <div
        className="pointer-events-none fixed inset-x-0 bottom-6 z-[200] flex flex-col items-center gap-2 px-4 sm:bottom-8 sm:right-8 sm:left-auto sm:items-end"
        role="status"
        aria-live="polite"
        aria-relevant="additions"
      >
        <AnimatePresence initial={false}>
          {items.map((t) => (
            <motion.div
              key={t.id}
              layout
              initial={{ opacity: 0, y: 24, scale: 0.96 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: 12, scale: 0.96, transition: { duration: 0.18 } }}
              transition={{ type: "spring", stiffness: 380, damping: 28 }}
              className={cn(
                "pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-2xl px-4 py-3 backdrop-blur-xl",
                "border shadow-[0_24px_60px_-20px_rgba(15,23,42,0.45)]",
                t.tone === "success"
                  ? "bg-[var(--color-success-soft)]/95 border-[var(--color-success)]/30 text-[var(--color-success)]"
                  : t.tone === "error"
                    ? "bg-[var(--color-coral-soft)]/95 border-[var(--color-coral)]/30 text-[var(--color-coral)]"
                    : "glass-strong text-[var(--color-ink-900)]",
              )}
            >
              <span className="mt-[2px] shrink-0">
                {t.tone === "success" ? (
                  <CheckCircle2 className="h-4 w-4" strokeWidth={2.4} />
                ) : t.tone === "error" ? (
                  <AlertCircle className="h-4 w-4" strokeWidth={2.4} />
                ) : (
                  <span className="block h-2 w-2 rounded-full bg-current" />
                )}
              </span>
              <p className="flex-1 text-[13px] font-medium leading-snug">{t.message}</p>
              {t.action && (
                <button
                  type="button"
                  onClick={() => {
                    t.action!.onClick();
                    dismiss(t.id);
                  }}
                  className="rounded-md bg-white/40 px-2 py-1 text-[11px] font-bold uppercase tracking-[0.08em] transition-colors hover:bg-white/70 dark:bg-white/10 dark:hover:bg-white/20"
                >
                  {t.action.label}
                </button>
              )}
              <button
                type="button"
                onClick={() => dismiss(t.id)}
                className="shrink-0 rounded-md p-1 opacity-60 transition-opacity hover:opacity-100"
                aria-label="Dismiss"
              >
                <X className="h-3.5 w-3.5" strokeWidth={2.4} />
              </button>
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </Ctx.Provider>
  );
}

export function useToast() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useToast must be used inside <ToastProvider>");
  return ctx;
}
