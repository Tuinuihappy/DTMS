"use client";

import { AlertTriangle, ArrowDown } from "lucide-react";
import type { TripMissionDto } from "@/lib/api/trips";
import { resolveRiot3ErrorAction } from "@/lib/vendor/riot3-error-codes";

export const MISSION_TIMELINE_ANCHOR = "mission-timeline-anchor";

// A mission is "alerting" when its latest state is anything operators need
// to react to — not the happy-path PROCESSING/FINISHED. Empty/whitespace
// states also fall through to alerting so we don't silently swallow
// malformed payloads.
export function isAlertMissionState(state: string | null | undefined): boolean {
  if (!state) return true;
  const s = state.trim().toUpperCase();
  return s !== "" && s !== "PROCESSING" && s !== "FINISHED";
}

// Pick the latest event per missionKey (by changeStateTime, with
// receivedAt as tiebreaker) so a mission that went PROCESSING → FAILED →
// FINISHED doesn't surface its historical FAILED as a still-alerting
// issue. Only missions whose *current* state is alerting are returned.
export function getFailingMissions(missions: TripMissionDto[]): TripMissionDto[] {
  const latestByKey = new Map<string, TripMissionDto>();
  for (const m of missions) {
    const existing = latestByKey.get(m.missionKey);
    if (!existing || isLater(m, existing)) {
      latestByKey.set(m.missionKey, m);
    }
  }
  return Array.from(latestByKey.values())
    .filter((m) => isAlertMissionState(m.state))
    .sort((a, b) => a.missionIndex - b.missionIndex);
}

function isLater(a: TripMissionDto, b: TripMissionDto): boolean {
  const ac = new Date(a.changeStateTime).getTime();
  const bc = new Date(b.changeStateTime).getTime();
  if (ac !== bc) return ac > bc;
  return new Date(a.receivedAt).getTime() > new Date(b.receivedAt).getTime();
}

// Banner shown at the top of the Trip drawer when any mission is in an
// alerting state (FAILED / HANG / REJECTED / CANCELED / unknown). One row
// per failing mission so operators see exactly what's wrong and can jump
// to the Mission Timeline for full context.
export function MissionFailureAlert({
  missions,
}: {
  missions: TripMissionDto[];
}) {
  const failing = getFailingMissions(missions);
  if (failing.length === 0) return null;

  const scrollToTimeline = () => {
    const el = document.getElementById(MISSION_TIMELINE_ANCHOR);
    if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  return (
    <div className="rounded-xl bg-[#fde0db] px-4 py-3 text-[12.5px] text-[var(--color-coral)] dark:bg-[#3a1a17]">
      <div className="flex items-start gap-2">
        <AlertTriangle className="mt-[2px] h-4 w-4 flex-shrink-0" strokeWidth={2.4} />
        <div className="min-w-0 flex-1">
          <div className="text-[10.5px] font-semibold uppercase tracking-[0.06em] opacity-80">
            Vendor mission {failing.length === 1 ? "failed" : `issues (${failing.length})`}
          </div>
          <ul className="mt-1.5 space-y-1.5">
            {failing.map((m, idx) => {
              const action = resolveRiot3ErrorAction(m.resultCode);
              return (
                <li key={`${m.missionKey}-${m.state}-${idx}`} className="font-medium">
                  <span className="font-mono text-[11px] uppercase tracking-[0.04em] opacity-80">
                    #{m.missionIndex + 1} {m.missionType}
                    {m.stationName ? ` → ${m.stationName}` : ""}
                  </span>
                  <span className="ml-1.5 rounded-full bg-white/60 px-1.5 py-[1px] text-[10.5px] font-semibold uppercase tracking-[0.06em] dark:bg-white/[0.08]">
                    {m.state}
                  </span>
                  {/* Action line (mapped) takes top billing — it tells the
                      operator what to do. Raw code + vendor message stays
                      visible below as a smaller secondary line so IT
                      escalations still have the original payload. */}
                  {action && (
                    <div className="mt-0.5 text-[12px] font-semibold">
                      {action}
                    </div>
                  )}
                  {(m.resultCode || m.errorMessage) && (
                    <div className="mt-0.5 text-[11px] font-normal opacity-75">
                      {m.resultCode && (
                        <span className="font-mono">{m.resultCode}</span>
                      )}
                      {m.resultCode && m.errorMessage && <span> · </span>}
                      {m.errorMessage}
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
          <button
            type="button"
            onClick={scrollToTimeline}
            className="mt-2 inline-flex items-center gap-1 rounded-full bg-white/70 px-2.5 py-1 text-[10.5px] font-semibold uppercase tracking-[0.06em] transition-colors hover:bg-white dark:bg-white/[0.08] dark:hover:bg-white/[0.14]"
          >
            <ArrowDown className="h-3 w-3" strokeWidth={2.4} />
            ดูรายละเอียดด้านล่าง
          </button>
        </div>
      </div>
    </div>
  );
}
