"use client";

import { ArrowRight, CircleAlert, MapPin, Zap } from "lucide-react";
import { cn } from "@/lib/utils";
import type { TripMissionDto } from "@/lib/api/trips";
import { resolveRiot3ErrorAction } from "@/lib/vendor/riot3-error-codes";
import {
  parseActCode,
  useActionNameIndex,
  type ActionNameIndex,
} from "@/lib/vendor/riot3-action-names";
import { sortMissionRows } from "@/lib/mission-order";
import { DateTime } from "@/components/primitives/date-time";
import { MissionStateBadge } from "./badges";

// Renders the per-mission timeline returned by /trips/{id}/details.
// Each row is a single (mission × state) tuple — a mission that goes
// PROCESSING → FINISHED produces two rows, which is what the audit
// wants to see. Rows are ordered by changeStateTime (the real vendor
// state-change time, reliable from both the reconciler and the sub-task
// webhook). missionIndex is NOT usable for ordering/numbering: the sub-task
// webhook payload carries no index so those rows all hardcode 0. Row numbers
// are the sorted position, not missionIndex, for the same reason.
export function MissionTimeline({
  missions,
}: {
  missions: TripMissionDto[];
}) {
  // ACT rows carry only the RIOT3 code ("ACT [4,1,0]") — resolve the
  // operator-named ActionTemplate for the same [id,param0,param1] triple
  // and show name + code together. Fetched once, only when an ACT row
  // exists; unresolved codes render as-is.
  const hasActRows = missions.some((m) => parseActCode(m.actionName) !== null);
  const actionNames = useActionNameIndex(hasActRows);

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

  // Shared comparator (lib/mission-order) — the failure banner numbers rows
  // from the same sort, so "#12" up there is guaranteed to be row #12 here.
  const sorted = sortMissionRows(missions);

  return (
    <ol className="relative space-y-3 pl-5">
      {/* vertical rail */}
      <span
        aria-hidden
        className="absolute left-[7px] top-2 bottom-2 w-px bg-[var(--color-ink-100)] dark:bg-white/10"
      />
      {sorted.map((m, idx) => (
        <MissionRow
          key={`${m.missionKey}-${m.state}-${m.occurrence ?? 1}-${idx}`}
          mission={m}
          seq={idx + 1}
          actionNames={actionNames}
        />
      ))}
    </ol>
  );
}

function MissionRow({
  mission,
  seq,
  actionNames,
}: {
  mission: TripMissionDto;
  seq: number;
  actionNames: ActionNameIndex;
}) {
  const isMove = mission.missionType.toUpperCase() === "MOVE";
  const Icon = isMove ? MapPin : Zap;
  const isFailed = ["FAILED"].includes(mission.state.toUpperCase());
  const hasError = mission.errorMessage || mission.resultCode === "1";
  const action = resolveRiot3ErrorAction(mission.resultCode);
  // "ACT [4,1,0]" → template name ("LIFTUP WITH CAMERA") + code chip.
  // Unresolved (index still loading / unknown code) → raw actionName only.
  const actCode = parseActCode(mission.actionName);
  const resolvedActionName = actCode ? actionNames.get(actCode) : undefined;

  return (
    <li className="relative">
      {/* dot on the rail */}
      <span
        aria-hidden
        className={cn(
          "absolute -left-[18px] top-1 inline-flex h-[14px] w-[14px] items-center justify-center rounded-full ring-4",
          isFailed
            ? "bg-[var(--color-coral)] ring-[var(--color-coral-soft)]"
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
                #{seq}
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
                  {resolvedActionName ? (
                    <>
                      <span className="text-[11.5px] font-medium text-[var(--color-ink-700)]">
                        {resolvedActionName}
                      </span>
                      <span className="font-mono text-[10.5px] text-[var(--color-ink-400)]">
                        [{actCode}]
                      </span>
                    </>
                  ) : (
                    <span className="font-mono text-[11.5px] font-medium text-[var(--color-ink-700)]">
                      {mission.actionName}
                    </span>
                  )}
                </>
              )}
            </div>
            {action && (
              <div className="mt-1.5 flex items-start gap-1 text-[11.5px] font-semibold text-[var(--color-coral)]">
                <CircleAlert
                  className="mt-0.5 h-3 w-3 flex-shrink-0"
                  strokeWidth={2.4}
                />
                <span>{action}</span>
              </div>
            )}
            {hasError && mission.errorMessage && (
              <div
                className={cn(
                  "flex items-start gap-1 text-[11px] text-[var(--color-coral)] opacity-75",
                  action ? "mt-0.5 pl-4" : "mt-1.5",
                )}
              >
                {!action && (
                  <CircleAlert
                    className="mt-0.5 h-3 w-3 flex-shrink-0"
                    strokeWidth={2.4}
                  />
                )}
                <span>{mission.errorMessage}</span>
              </div>
            )}
          </div>
          <div className="flex flex-shrink-0 items-center gap-1.5">
            {/* RC3 — occurrence > 1 marks a RIOT-side retry of this mission
                state; the row the old unique index used to drop silently. */}
            {(mission.occurrence ?? 1) > 1 && (
              <span className="rounded-full bg-amber-100 px-2 py-[2px] text-[10px] font-semibold uppercase tracking-[0.06em] text-amber-700 dark:bg-amber-500/15 dark:text-amber-400">
                retry #{mission.occurrence}
              </span>
            )}
            <MissionStateBadge state={mission.state} />
          </div>
        </div>
        <div className="mt-1.5 flex items-center gap-3 text-[10.5px] tabular-nums text-[var(--color-ink-400)]">
          <DateTime
            value={mission.changeStateTime}
            variant="datetime-seconds"
            showTooltip={false}
          />
          {mission.resultCode && (
            <span className="font-mono">code: {mission.resultCode}</span>
          )}
        </div>
      </div>
    </li>
  );
}
