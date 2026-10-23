"use client";

import { Loader2 } from "lucide-react";
import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Plain button with a pending state. Styling stays with the caller (tones
 * differ per surface); this owns only the behaviour every async button
 * needs:
 *
 * - `pending` disables it, sets aria-busy and shows a spinner. With an
 *   `icon`, the spinner takes the icon's slot; without one, the label goes
 *   invisible under a centred spinner. Either way the label never swaps,
 *   so the button keeps its width — no jump when the request starts.
 * - A CSS press (`active:scale-[.98]`) instead of motion's whileTap, so it
 *   can't fight a motion transform on a parent.
 */
export function Button({
  pending = false,
  icon,
  spinnerClassName = "h-3.5 w-3.5",
  disabled,
  className,
  children,
  type = "button",
  ...rest
}: {
  pending?: boolean;
  /** Leading icon; replaced by the spinner while pending. */
  icon?: ReactNode;
  /** Size the spinner like the icon it replaces. */
  spinnerClassName?: string;
  children?: ReactNode;
} & ButtonHTMLAttributes<HTMLButtonElement>) {
  const spinner = (
    <Loader2 className={cn("animate-spin", spinnerClassName)} strokeWidth={2.4} aria-hidden />
  );
  return (
    <button
      type={type}
      disabled={disabled || pending}
      aria-busy={pending || undefined}
      className={cn(
        "relative transition-[background-color,box-shadow,color,opacity,transform] duration-150",
        "enabled:active:scale-[.98]",
        className,
      )}
      {...rest}
    >
      {icon !== undefined ? (
        <>
          {pending ? spinner : icon}
          {children}
        </>
      ) : (
        <>
          {/* display:contents keeps the caller's flex gap; visibility
              still inherits through it, text nodes included. */}
          <span className={cn("contents", pending && "invisible")}>{children}</span>
          {pending && (
            <span className="absolute inset-0 grid place-items-center">{spinner}</span>
          )}
        </>
      )}
    </button>
  );
}
