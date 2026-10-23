"use client";

// In-app replacement for window.confirm(). The native box ignores the
// theme, can't show structured consequences, and isn't part of the layer
// stack (Esc, focus return). Usage keeps the old shape:
//
//   const { confirm, confirmDialog } = useConfirm();
//   if (!(await confirm({ title: "Delete X?", body: …, tone: "danger" }))) return;
//   …
//   return <>{…}{confirmDialog}</>;

import { AlertTriangle, HelpCircle, X } from "lucide-react";
import { motion } from "motion/react";
import { useCallback, useRef, useState } from "react";
import { ModalLayer } from "@/components/primitives/modal-layer";
import { spring, T } from "@/lib/motion";
import { cn } from "@/lib/utils";

export type ConfirmOptions = {
  title: string;
  body?: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** "danger": coral button, focus opens on Cancel. */
  tone?: "danger" | "default";
};

export function useConfirm() {
  const [opts, setOpts] = useState<ConfirmOptions | null>(null);
  const [open, setOpen] = useState(false);
  const resolver = useRef<((ok: boolean) => void) | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  const settle = useCallback((ok: boolean) => {
    resolver.current?.(ok);
    resolver.current = null;
    setOpen(false);
  }, []);

  const confirm = useCallback((o: ConfirmOptions) => {
    // A second ask while one is open answers the first "no".
    resolver.current?.(false);
    setOpts(o);
    setOpen(true);
    return new Promise<boolean>((resolve) => {
      resolver.current = resolve;
    });
  }, []);

  const danger = opts?.tone === "danger";
  // Danger opens focus on Cancel (ModalLayer's irreversible intent).
  const focusProps = danger
    ? ({ intent: "irreversible", safeRef: cancelRef } as const)
    : ({ intent: "default" } as const);

  const confirmDialog = (
    <ModalLayer
      open={open}
      onClose={() => settle(false)}
      {...focusProps}
      backdropClassName="bg-[var(--color-ink-900)]/50"
    >
      {({ panelProps, titleId, panelStyle }) =>
        open &&
        opts && (
          <div
            style={panelStyle}
            key="confirm-dialog"
            className="pointer-events-none fixed inset-0 flex items-center justify-center p-4"
          >
            <motion.div
              {...panelProps}
              initial={{ opacity: 0, scale: 0.96, y: 8 }}
              animate={{ opacity: 1, scale: 1, y: 0, transition: spring.dialog }}
              exit={{ opacity: 0, scale: 0.98, transition: T.exit }}
              className="pointer-events-auto relative w-full max-w-md overflow-hidden rounded-[var(--radius-xl)] border border-[var(--color-ink-100)] bg-[var(--color-popover)] shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)] outline-none dark:border-white/[0.08]"
            >
              <header className="flex items-start gap-3 px-6 pt-5">
                <span
                  className={cn(
                    "grid h-10 w-10 shrink-0 place-items-center rounded-full",
                    danger
                      ? "bg-[var(--color-coral-soft)] text-[var(--color-coral)]"
                      : "bg-[var(--color-pastel-sky)] text-[var(--color-brand-500)]",
                  )}
                >
                  {danger ? (
                    <AlertTriangle className="h-5 w-5" strokeWidth={2.2} />
                  ) : (
                    <HelpCircle className="h-5 w-5" strokeWidth={2.2} />
                  )}
                </span>
                <h2
                  id={titleId}
                  className="font-display mt-1.5 flex-1 text-[1.1rem] font-semibold text-[var(--color-ink-900)]"
                >
                  {opts.title}
                </h2>
                <button
                  type="button"
                  onClick={() => settle(false)}
                  className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                  aria-label="Close"
                >
                  <X className="h-4 w-4" strokeWidth={2.4} />
                </button>
              </header>

              {opts.body && (
                <div className="px-6 pb-2 pt-3 text-[12.5px] leading-relaxed text-[var(--color-ink-600)]">
                  {opts.body}
                </div>
              )}

              <footer className="mt-3 flex items-center justify-end gap-2 border-t border-[var(--color-ink-100)] px-6 py-4 dark:border-white/[0.06]">
                <button
                  ref={cancelRef}
                  type="button"
                  onClick={() => settle(false)}
                  className="rounded-full bg-[var(--color-ink-100)] px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-[var(--color-ink-200)] dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
                >
                  {opts.cancelLabel ?? "Cancel"}
                </button>
                <button
                  type="button"
                  onClick={() => settle(true)}
                  className={cn(
                    "rounded-full px-4 py-2 text-[12px] font-semibold text-white transition-[background-color,box-shadow] active:scale-[0.97]",
                    danger
                      ? "bg-[var(--color-coral)] hover:shadow-[0_14px_36px_-12px_rgba(220,80,80,0.6)]"
                      : "bg-[var(--color-brand-500)] hover:shadow-[0_14px_36px_-12px_rgba(40,90,220,0.5)]",
                  )}
                >
                  {opts.confirmLabel ?? "Confirm"}
                </button>
              </footer>
            </motion.div>
          </div>
        )
      }
    </ModalLayer>
  );

  return { confirm, confirmDialog };
}

/** Bulleted consequence list for a confirm body. */
export function ConfirmPoints({ intro, points }: { intro?: React.ReactNode; points: React.ReactNode[] }) {
  return (
    <>
      {intro && <p>{intro}</p>}
      <ul className={cn("list-disc space-y-1 pl-5", intro && "mt-2")}>
        {points.map((p, i) => (
          <li key={i}>{p}</li>
        ))}
      </ul>
    </>
  );
}
