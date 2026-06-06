"use client";

import { ArrowRight, CircleAlert, MapPin, Zap } from "lucide-react";
import { cn } from "@/lib/utils";
import type { TripMissionDto } from "@/lib/api/trips";
import { MissionStateBadge } from "./badges";

// Renders the per-mission timeline returned by /trips/{id}/details.
// Each row is a single (mission × state) tuple — a mission that goes
// PROCESSING → FINISHED produces two rows, which is what the audit
// wants to see. Rows are ordered by missionIndex then changeStateTime
// (server already sorts; we sort client-side as a defensive double-check).
export function MissionTimeline({
  missions,
}: {
  missions: TripMissionDto[];
}) {
  if (missions.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-[var(--color-ink-100)] px-4 py-6 text-center text-[12px] text-[var(--color-ink-500)] dark:border-white/10">
        No mission events yet.
        <div className="mt-1 text-[11px] text-[var(--color-ink-400)]">
          Sub-task webhooks populate this list as the robot moves.
        </div>
      </div>
    );
  }

  const sorted = [...missions].sort((a, b) => {
    if (a.missionIndex !== b.missionIndex) return a.missionIndex - b.missionIndex;
    return new Date(a.changeStateTime).getTime() - new Date(b.changeStateTime).getTime();
  });

  return (
    <ol className="relative space-y-3 pl-5">
      {/* vertical rail */}
      <span
        aria-hidden
        className="absolute left-[7px] top-2 bottom-2 w-px bg-[var(--color-ink-100)] dark:bg-white/10"
      />
      {sorted.map((m, idx) => (
        <MissionRow key={`${m.missionKey}-${m.state}-${idx}`} mission={m} />
      ))}
    </ol>
  );
}

function MissionRow({ mission }: { mission: TripMissionDto }) {
  const isMove = mission.missionType.toUpperCase() === "MOVE";
  const Icon = isMove ? MapPin : Zap;
  const isFailed = ["FAILED"].includes(mission.state.toUpperCase());
  const hasError = mission.errorMessage || mission.resultCode === "1";

  return (
    <li className="relative">
      {/* dot on the rail */}
      <span
        aria-hidden
        className={cn(
          "absolute -left-[18px] top-1 inline-flex h-[14px] w-[14px] items-center justify-center rounded-full ring-4",
          isFailed
            ? "bg-[var(--color-coral)] ring-[#fde0db] dark:ring-[#3a1a17]"
            : "bg-[var(--color-surface)] ring-[var(--color-ink-100)] dark:ring-white/10",
        )}
      >
        <Icon
          className={cn(
            "h-[8px] w-[8px]",
            isFailed ? "text-white" : "text-[var(--color-ink-500)]",
          )}
          strokeWidth={2.8}
        />
      </span>

      <div className="ml-1 rounded-xl bg-[var(--color-ink-100)]/30 px-3 py-2.5 dark:bg-white/[0.03]">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <span className="text-[11px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-400)]">
                #{mission.missionIndex + 1}
              </span>
              <span className="text-[12px] font-semibold text-[var(--color-ink-900)]">
                {mission.missionType}
              </span>
              {mission.stationName && (
                <>
                  <ArrowRight
                    className="h-3 w-3 text-[var(--color-ink-300)]"
                    strokeWidth={2.4}
                  />
                  <span className="font-mono text-[11.5px] font-medium text-[var(--color-ink-700)]">
                    {mission.stationName}
                  </span>
                </>
              )}
              {mission.actionName && !mission.stationName && (
                <>
                  <ArrowRight
                    className="h-3 w-3 text-[var(--color-ink-300)]"
                    strokeWidth={2.4}
                  />
                  <span className="font-mono text-[11.5px] font-medium text-[var(--color-ink-700)]">
                    {mission.actionName}
                  </span>
                </>
              )}
            </div>
            {hasError && mission.errorMessage && (
              <div className="mt-1.5 flex items-start gap-1 text-[11px] text-[var(--color-coral)]">
                <CircleAlert
                  className="mt-0.5 h-3 w-3 flex-shrink-0"
                  strokeWidth={2.4}
                />
                <span>{mission.errorMessage}</span>
              </div>
            )}
          </div>
          <MissionStateBadge state={mission.state} />
        </div>
        <div className="mt-1.5 flex items-center gap-3 text-[10.5px] tabular-nums text-[var(--color-ink-400)]">
          <span title="Vendor's change-state time">
            {new Date(mission.changeStateTime).toLocaleString()}
          </span>
          {mission.resultCode && (
            <span className="font-mono">code: {mission.resultCode}</span>
          )}
        </div>
      </div>
    </li>
  );
}
