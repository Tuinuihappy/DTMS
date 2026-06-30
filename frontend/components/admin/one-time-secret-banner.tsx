"use client";

import { useState } from "react";
import { Check, Copy, Eye, EyeOff, X } from "lucide-react";

/**
 * Phase S.6 — banner that shows a freshly minted plaintext secret ONCE
 * (on create-system + rotate-credential responses). The backend never
 * returns this value again, so the UI must communicate that clearly:
 *
 * - Plaintext is masked by default; reveal with the Eye toggle
 * - Copy button writes to clipboard with a 2s "Copied!" confirmation
 * - Dismissable; once dismissed the plaintext is gone from React state
 * - Warning text ("Save this now — won't show again") sits above the box
 *
 * Mount in the same place where the success would normally show. The
 * parent owns the visible/hidden state — pass `onDismiss` to remove the
 * banner from your local state when the user closes it.
 */
export function OneTimeSecretBanner({
  title,
  secret,
  helpText,
  onDismiss,
}: {
  title: string;
  secret: string;
  helpText?: string;
  onDismiss: () => void;
}) {
  const [revealed, setRevealed] = useState(false);
  const [copied, setCopied] = useState(false);

  const masked = secret.length <= 12 ? "•".repeat(secret.length) : secret.slice(0, 8) + "•".repeat(20) + secret.slice(-4);

  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard not available — user can still reveal + select */
    }
  };

  return (
    <div className="rounded-lg border border-amber-400/60 bg-amber-50 px-4 py-4 dark:border-amber-500/40 dark:bg-amber-950/30">
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h4 className="text-sm font-semibold text-amber-900 dark:text-amber-200">{title}</h4>
          </div>
          <p className="mt-1 text-xs text-amber-800 dark:text-amber-300">
            Save this value now — it will not be shown again. Storing it
            anywhere else is the only way to recover it.
          </p>
          {helpText && (
            <p className="mt-1 text-[11px] text-amber-700/80 dark:text-amber-400/80">{helpText}</p>
          )}

          <div className="mt-3 flex items-stretch gap-2">
            <code
              className="flex-1 select-all break-all rounded border border-amber-300 bg-white px-3 py-2 font-mono text-xs text-amber-950 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-100"
              data-testid="otsb-secret"
            >
              {revealed ? secret : masked}
            </code>
            <button
              type="button"
              onClick={() => setRevealed((r) => !r)}
              className="rounded border border-amber-300 bg-white px-3 text-amber-700 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-300 dark:hover:bg-amber-900"
              aria-label={revealed ? "Hide secret" : "Reveal secret"}
            >
              {revealed ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
            <button
              type="button"
              onClick={onCopy}
              className="rounded border border-amber-300 bg-white px-3 text-amber-700 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-300 dark:hover:bg-amber-900"
              aria-label="Copy secret to clipboard"
            >
              {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
            </button>
          </div>
        </div>
        <button
          type="button"
          onClick={onDismiss}
          className="rounded p-1 text-amber-700 hover:bg-amber-100 dark:text-amber-300 dark:hover:bg-amber-900"
          aria-label="Dismiss"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}
