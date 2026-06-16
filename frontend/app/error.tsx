"use client";

import { AlertTriangle, RefreshCw } from "lucide-react";
import { useEffect } from "react";

export default function RouteError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("[route-error]", error);
  }, [error]);

  return (
    <div className="min-h-screen flex items-center justify-center px-6">
      <div className="max-w-md w-full text-center space-y-5">
        <div className="inline-flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--color-coral)]/10 text-[var(--color-coral)]">
          <AlertTriangle className="h-7 w-7" strokeWidth={2} />
        </div>
        <div className="space-y-2">
          <h1 className="text-xl font-semibold text-[var(--color-ink-900)] dark:text-white">
            Something went wrong on this page
          </h1>
          <p className="text-sm text-[var(--color-ink-600)] dark:text-white/60">
            {error.message || "An unexpected error occurred."}
          </p>
          {error.digest && (
            <p className="text-[11px] font-mono text-[var(--color-ink-400)]">
              ref: {error.digest}
            </p>
          )}
        </div>
        <div className="flex gap-2 justify-center pt-2">
          <button
            onClick={reset}
            className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-ink-900)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--color-ink-800)] transition-colors"
          >
            <RefreshCw className="h-3.5 w-3.5" strokeWidth={2.2} />
            Try again
          </button>
          <button
            onClick={() => window.history.back()}
            className="inline-flex items-center rounded-full border border-[var(--color-ink-200)] px-4 py-2 text-sm font-medium text-[var(--color-ink-700)] hover:bg-[var(--color-ink-50)] transition-colors"
          >
            Go back
          </button>
        </div>
      </div>
    </div>
  );
}
