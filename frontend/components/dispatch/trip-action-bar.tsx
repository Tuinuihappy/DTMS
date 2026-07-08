"use client";

import { AlertTriangle, ChevronsRight, Pause, Play, Repeat2, X } from "lucide-react";
import { useState } from "react";
import { cancelTrip, pauseTrip, resumeTrip, retryTrip, type TripStatus } from "@/lib/api/trips";
import { cn } from "@/lib/utils";
import { PassRobotDialog } from "./pass-robot-dialog";
import { RaiseExceptionDialog } from "./raise-exception-dialog";

// Contextual action toolbar for a Trip. Buttons enable/disable based on
// the current Trip status so operators can't fire commands the domain
// will reject anyway. Each action delegates to the trips API client; on
// success the parent decides what to do (refetch + close, etc).
//
// PASS is grouped separately on the left because it's a robot-level
// interaction (operator nudges the robot past a checkpoint) — distinct
// from the lifecycle group on the right which mutates Trip.Status.
type Action = "cancel" | "pause" | "resume" | "retry" | "pass" | "exception";

export function TripActionBar({
  tripId,
  status,
  vendorVehicleKey,
  hasVendorIssue = false,
  onAction,
}: {
  tripId: string;
  status: TripStatus;
  vendorVehicleKey?: string | null;
  // When true, the trip has a vendor-side mission stuck in an alerting
  // state (FAILED / HANG / REJECTED). Resume is still allowed — DTMS
  // status is Paused so the command is technically valid — but the
  // button surfaces warning styling + a tooltip so the operator knows
  // RIOT may reject until the robot is cleared at the floor.
  hasVendorIssue?: boolean;
  onAction?: (action: Action, payload?: { newTripId?: string }) => void;
}) {
  const [busy, setBusy] = useState<Action | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [passDialogOpen, setPassDialogOpen] = useState(false);
  const [exceptionDialogOpen, setExceptionDialogOpen] = useState(false);

  const canCancel = status === "Created" || status === "InProgress" || status === "Paused";
  const canPause = status === "InProgress";
  const canResume = status === "Paused";
  const canRetry = status === "Cancelled";
  const canPass = status === "InProgress" && !!vendorVehicleKey;
  // Annotate a problem on an in-flight trip (doesn't change Trip.Status).
  const canRaiseException =
    status === "Created" || status === "InProgress" || status === "Paused";

  const run = async (action: Exclude<Action, "pass">, fn: () => Promise<unknown>) => {
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
      <div className="flex flex-wrap items-center gap-2">
        <ActionButton
          icon={ChevronsRight}
          label="Pass"
          tone="sky"
          disabled={!canPass || busy !== null}
          busy={false}
          onClick={() => setPassDialogOpen(true)}
          title={
            canPass
              ? "Acknowledge robot waiting at checkpoint"
              : "ใช้ได้เฉพาะตอน Trip InProgress และมี Vehicle key"
          }
        />
        <ActionButton
          icon={AlertTriangle}
          label="Flag"
          tone="amber"
          disabled={!canRaiseException || busy !== null}
          busy={false}
          onClick={() => setExceptionDialogOpen(true)}
          title={
            canRaiseException
              ? "Raise an exception on this trip"
              : "ใช้ได้เฉพาะตอน Trip ยัง active (Created/InProgress/Paused)"
          }
        />
        <span
          aria-hidden
          className="h-5 w-px bg-[var(--color-ink-200)] dark:bg-white/[0.08]"
        />
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
          tone={canResume && hasVendorIssue ? "amber" : "success"}
          disabled={!canResume || busy !== null}
          busy={busy === "resume"}
          onClick={() => run("resume", () => resumeTrip(tripId))}
          title={
            canResume && hasVendorIssue
              ? "Vendor mission ติด FAILED/HANG — Resume อาจ fail จนกว่าจะเคลียร์หุ่นที่หน้างาน"
              : undefined
          }
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
        <div className="rounded-md bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
          {error}
        </div>
      )}
      {canPass && (
        <PassRobotDialog
          open={passDialogOpen}
          tripId={tripId}
          vendorVehicleKey={vendorVehicleKey!}
          onClose={() => setPassDialogOpen(false)}
          onPassed={() => onAction?.("pass")}
        />
      )}
      <RaiseExceptionDialog
        open={exceptionDialogOpen}
        tripId={tripId}
        onClose={() => setExceptionDialogOpen(false)}
        onRaised={() => onAction?.("exception")}
      />
    </div>
  );
}

type Tone = "amber" | "success" | "lavender" | "coral" | "sky";

const TONE_CLS: Record<Tone, string> = {
  amber: "bg-[var(--color-amber-soft)] text-[var(--color-amber)] hover:bg-[var(--color-amber-soft)]/80",
  success:
    "bg-[var(--color-success-soft)] text-[var(--color-success)] hover:bg-[var(--color-success-soft)]/80",
  lavender:
    "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80",
  coral: "bg-[var(--color-coral-soft)] text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]/80",
  sky: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)] hover:bg-[var(--color-pastel-sky)]/80",
};

function ActionButton({
  icon: Icon,
  label,
  tone,
  disabled,
  busy,
  onClick,
  title,
}: {
  icon: React.ComponentType<{ className?: string; strokeWidth?: number }>;
  label: string;
  tone: Tone;
  disabled?: boolean;
  busy?: boolean;
  onClick: () => void;
  title?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
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
