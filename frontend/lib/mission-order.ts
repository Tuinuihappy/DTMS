// Single source of truth for mission-row ordering and row numbering.
//
// The Mission Timeline numbers rows by their SORTED position (missionIndex
// is unusable — sub-task webhook rows all hardcode 0), and the failure
// banner must show the SAME number so an operator can jump from
// "#12 MOVE FAILED" straight to row #12 in the timeline. Two components
// sorting with two comparators is how those numbers drift apart, so both
// import from here.
//
// Ordering mirrors the backend read query exactly
// (TripMissionEventRepository.GetByTripIdAsync: ChangeStateTime →
// ReceivedAt). The receivedAt tie-break matters client-side: live-pushed
// rows are APPENDED to the fetched list, so a changeStateTime-only sort
// left equal-timestamp rows in arrival order, not backend order.

import type { TripMissionDto } from "@/lib/api/trips";

export function compareMissionRows(a: TripMissionDto, b: TripMissionDto): number {
  const byChange =
    new Date(a.changeStateTime).getTime() - new Date(b.changeStateTime).getTime();
  if (byChange !== 0) return byChange;
  return new Date(a.receivedAt).getTime() - new Date(b.receivedAt).getTime();
}

export function sortMissionRows(missions: TripMissionDto[]): TripMissionDto[] {
  return [...missions].sort(compareMissionRows);
}

// (missionKey, state) is unique across a trip's rows — guaranteed by the
// backend unique index and re-enforced by the drawer's live-push dedup —
// so it can key a row → display-number map.
export function missionRowKey(m: TripMissionDto): string {
  return `${m.missionKey}|${m.state}`;
}

// Map every row to its 1-based position in the shared sort order. Callers
// should treat a missing lookup as "don't show a number" rather than
// falling back to missionIndex (which would reintroduce the always-#1 bug).
export function buildRowNumberIndex(
  missions: TripMissionDto[],
): ReadonlyMap<string, number> {
  const index = new Map<string, number>();
  sortMissionRows(missions).forEach((m, i) => index.set(missionRowKey(m), i + 1));
  return index;
}
