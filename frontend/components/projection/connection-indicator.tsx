"use client";

import { cn } from "@/lib/utils";

// Phase P0 Day 5 — small dot + label rendering the SignalR hub state.
// Drop this into any panel header that has a useHubSubscription
// underneath — the `connected` boolean it returns is exactly what feeds
// this component.
//
// States visually distinct:
//   - connected     → green dot, pulse animation, "Live"
//   - connecting    → amber dot, pulse, "Connecting…"
//   - disconnected  → grey dot, no pulse, "Offline"
//   - error         → coral dot, no pulse, "Disconnected"

export type ConnectionState = "connected" | "connecting" | "disconnected" | "error";

export function ConnectionIndicator({
  state,
  label,
  className,
}: {
  state: ConnectionState;
  /** Optional override; defaults to a state-derived label. */
  label?: string;
  className?: string;
}) {
  const tone = TONE[state];
  const text = label ?? DEFAULT_LABEL[state];

  return (
    <span
      title={text}
      className={cn(
        "inline-flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.06em]",
        tone.text,
        className,
      )}
    >
      <span className="relative inline-flex h-1.5 w-1.5">
        <span className={cn("absolute inset-0 rounded-full", tone.dot)} />
        {(state === "connected" || state === "connecting") && (
          <span
            className={cn(
              "absolute inset-0 rounded-full animate-ping opacity-60",
              tone.dot,
            )}
          />
        )}
      </span>
      {text}
    </span>
  );
}

const TONE: Record<ConnectionState, { text: string; dot: string }> = {
  connected: {
    text: "text-[var(--color-success)]",
    dot: "bg-[var(--color-success)]",
  },
  connecting: {
    text: "text-[var(--color-amber)]",
    dot: "bg-[var(--color-amber)]",
  },
  disconnected: {
    text: "text-[var(--color-ink-500)]",
    dot: "bg-[var(--color-ink-400)]",
  },
  error: {
    text: "text-[var(--color-coral)]",
    dot: "bg-[var(--color-coral)]",
  },
};

const DEFAULT_LABEL: Record<ConnectionState, string> = {
  connected: "Live",
  connecting: "Connecting…",
  disconnected: "Offline",
  error: "Disconnected",
};
