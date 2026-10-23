"use client";

// App-wide toasts. Mounted once in app/providers.tsx, so a toast survives
// navigation and every screen shares one stack (the orders/jobs screens,
// templates and the facility map each used to hand-roll their own).
//
// Lifetimes:
//   - success / info: 3.8s, paused while hovered or focused.
//   - error: stays until dismissed — an operator who looked away must
//     still be able to read why something failed.
//   - with an action (Undo): exactly `durationMs`, NOT paused. The caller
//     runs its own timer for the real write; the toast must disappear the
//     moment that timer fires, or an Undo click lands on nothing. A bar
//     drains along the bottom so the window is visible.

import { AlertCircle, CheckCircle2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { spring, T } from "@/lib/motion";
import { cn } from "@/lib/utils";

export type ToastTone = "success" | "error" | "info";

export type ToastInput = {
  tone: ToastTone;
  message: string;
  /** Inline action — usually Undo for a delayed destructive write. */
  action?: { label: string; onClick: () => void };
  /** Lifetime override. Required in practice for action toasts so the
   *  toast and the caller's timer agree. */
  durationMs?: number;
};

type ToastItem = ToastInput & { id: string; lifeMs: number | null };

type ToastContext = {
  push: (t: ToastInput) => string;
  dismiss: (id: string) => void;
};

const Ctx = createContext<ToastContext | null>(null);

const MAX_VISIBLE = 4;

function lifetimeOf(t: ToastInput): number | null {
  if (t.durationMs != null) return t.durationMs;
  if (t.action) return 6_000;
  if (t.tone === "error") return null;
  return 3_800;
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);

  const dismiss = useCallback((id: string) => {
    setItems((prev) => prev.filter((x) => x.id !== id));
  }, []);

  const push = useCallback((t: ToastInput) => {
    const id = Math.random().toString(36).slice(2, 9);
    setItems((prev) => [...prev, { ...t, id, lifeMs: lifetimeOf(t) }].slice(-MAX_VISIBLE));
    return id;
  }, []);

  const ctx = useMemo(() => ({ push, dismiss }), [push, dismiss]);

  return (
    <Ctx.Provider value={ctx}>
      {children}
      <section
        aria-label="Notifications"
        // --toast-lift: set by anything parked at the bottom of the screen
        // (the orders bulk bar) so the stack sits above it.
        className="pointer-events-none fixed inset-x-0 bottom-[calc(1.5rem+var(--toast-lift,0px))] z-[200] flex flex-col items-center gap-2 px-4 transition-[bottom] duration-200 sm:right-8 sm:bottom-[calc(2rem+var(--toast-lift,0px))] sm:left-auto sm:items-end"
      >
        <AnimatePresence initial={false}>
          {items.map((t) => (
            <ToastCard key={t.id} toast={t} onDismiss={dismiss} />
          ))}
        </AnimatePresence>
      </section>
    </Ctx.Provider>
  );
}

function ToastCard({
  toast: t,
  onDismiss,
}: {
  toast: ToastItem;
  onDismiss: (id: string) => void;
}) {
  // Pausable timer: track what is left, restart on resume. Action toasts
  // never pause (see header).
  const pausable = !t.action;
  const [paused, setPaused] = useState(false);
  const remainingRef = useRef(t.lifeMs);
  const startedRef = useRef(0);

  useEffect(() => {
    if (remainingRef.current == null) return;
    if (paused && pausable) return;
    startedRef.current = Date.now();
    const timer = setTimeout(() => onDismiss(t.id), remainingRef.current);
    return () => {
      clearTimeout(timer);
      if (remainingRef.current != null) {
        remainingRef.current = Math.max(0, remainingRef.current - (Date.now() - startedRef.current));
      }
    };
  }, [paused, pausable, onDismiss, t.id]);

  const pause = () => setPaused(true);
  const resume = () => setPaused(false);

  return (
    <motion.div
      layout
      role={t.tone === "error" ? "alert" : "status"}
      initial={{ opacity: 0, y: 16, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1, transition: spring.dialog }}
      exit={{ opacity: 0, scale: 0.97, transition: T.exit }}
      onPointerEnter={pause}
      onPointerLeave={resume}
      onFocus={pause}
      onBlur={resume}
      className={cn(
        // Solid fills — a backdrop blur under a moving, layout-animated
        // card is re-rasterised every frame.
        "pointer-events-auto relative flex w-full max-w-sm items-start gap-3 overflow-hidden rounded-2xl border px-4 py-3",
        "shadow-[0_24px_60px_-20px_rgba(15,23,42,0.45)]",
        t.tone === "success"
          ? "border-[var(--color-success)]/30 bg-[var(--color-success-soft)] text-[var(--color-success)]"
          : t.tone === "error"
            ? "border-[var(--color-coral)]/30 bg-[var(--color-coral-soft)] text-[var(--color-coral)]"
            : "border-[var(--color-ink-200)] bg-[var(--color-popover)] text-[var(--color-ink-900)]",
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
            onDismiss(t.id);
          }}
          className="rounded-md bg-black/5 px-2 py-1 text-[11px] font-bold uppercase tracking-[0.08em] transition-colors hover:bg-black/10 dark:bg-white/10 dark:hover:bg-white/20"
        >
          {t.action.label}
        </button>
      )}
      <button
        type="button"
        onClick={() => onDismiss(t.id)}
        className="shrink-0 rounded-md p-1 opacity-60 transition-opacity hover:opacity-100"
        aria-label="Dismiss"
      >
        <X className="h-3.5 w-3.5" strokeWidth={2.4} />
      </button>
      {t.action && t.lifeMs != null && (
        // The undo window, draining. CSS animation so the reduced-motion
        // rule in globals.css stills it like everything else.
        <span
          aria-hidden
          className="toast-drain absolute inset-x-0 bottom-0 h-[3px] origin-left bg-current opacity-40"
          style={{ animationDuration: `${t.lifeMs}ms` }}
        />
      )}
    </motion.div>
  );
}

export function useToast() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useToast must be used inside <ToastProvider>");
  return ctx;
}
