"use client";

import { Pause, Play, Repeat2, X } from "lucide-react";
import { useState } from "react";
import { cancelTrip, pauseTrip, resumeTrip, retryTrip, type TripStatus } from "@/lib/api/trips";
import { cn } from "@/lib/utils";

// Contextual action toolbar for a Trip. Buttons enable/disable based on
// the current Trip status so operators can't fire commands the domain
// will reject anyway. Each action delegates to the trips API client; on
// success the parent decides what to do (refetch + close, etc).
type Action = "cancel" | "pause" | "resume" | "retry";

export function TripActionBar({
  tripId,
  status,
  onAction,
}: {
  tripId: string;
  status: TripStatus;
  onAction?: (action: Action, payload?: { newTripId?: string }) => void;
}) {
  const [busy, setBusy] = useState<Action | null>(null);
  const [error, setError] = useState<string | null>(null);

  const canCancel = status === "Created" || status === "InProgress" || status === "Paused";
  const canPause = status === "InProgress";
  const canResume = status === "Paused";
  const canRetry = status === "Cancelled";

  const run = async (action: Action, fn: () => Promise<unknown>) => {
    setBusy(action);
    setError(null);
    try {
      const result = await fn();
      onAction?.(
        action,
        action === "retry" && typeof result === "object" && result !== null && "newTripId" in result
          ? { newTripId: (result as { newTripId: string }).newTripId }
          : undefined,
      );
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        <ActionButton
          icon={Pause}
          label="Pause"
          tone="amber"
          disabled={!canPause || busy !== null}
          busy={busy === "pause"}
          onClick={() => run("pause", () => pauseTrip(tripId))}
        />
        <ActionButton
          icon={Play}
          label="Resume"
          tone="success"
          disabled={!canResume || busy !== null}
          busy={busy === "resume"}
          onClick={() => run("resume", () => resumeTrip(tripId))}
        />
        <ActionButton
          icon={Repeat2}
          label="Retry"
          tone="lavender"
          disabled={!canRetry || busy !== null}
          busy={busy === "retry"}
          onClick={() =>
            run("retry", () =>
              retryTrip(tripId, { source: "Manual", reason: "operator retry from UI" }),
            )
          }
        />
        <ActionButton
          icon={X}
          label="Cancel"
          tone="coral"
          disabled={!canCancel || busy !== null}
          busy={busy === "cancel"}
          onClick={() => run("cancel", () => cancelTrip(tripId, "Cancelled by operator"))}
        />
      </div>
      {error && (
        <div className="rounded-md bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
          {error}
        </div>
      )}
    </div>
  );
}

type Tone = "amber" | "success" | "lavender" | "coral";

const TONE_CLS: Record<Tone, string> = {
  amber: "bg-[var(--color-amber-soft)] text-[var(--color-amber)] hover:bg-[var(--color-amber-soft)]/80",
  success:
    "bg-[var(--color-success-soft)] text-[var(--color-success)] hover:bg-[var(--color-success-soft)]/80",
  lavender:
    "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80",
  coral: "bg-[#fde0db] text-[var(--color-coral)] hover:bg-[#fde0db]/80 dark:bg-[#3a1a17]",
};

function ActionButton({
  icon: Icon,
  label,
  tone,
  disabled,
  busy,
  onClick,
}: {
  icon: React.ComponentType<{ className?: string; strokeWidth?: number }>;
  label: string;
  tone: Tone;
  disabled?: boolean;
  busy?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[12px] font-semibold uppercase tracking-[0.06em] transition-all duration-200",
        disabled
          ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
          : TONE_CLS[tone],
        busy && "opacity-60",
      )}
    >
      <Icon className={cn("h-3.5 w-3.5", busy && "animate-spin")} strokeWidth={2.4} />
      {label}
    </button>
  );
}
