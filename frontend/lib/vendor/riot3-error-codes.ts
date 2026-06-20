// Map of RIOT3 errorCode (and a few detailCodes) → human-readable English
// action message. Codes catalogued from /api/v4/message/alarms on
// 2026-06-20; the count in each comment is how often the code appeared
// in the sample so the highest-traffic codes (brake error, manual mode,
// E-stop) are obvious when triaging or extending this map.
//
// Lookup order: errorCode first, then detailCode as fallback so a code
// that only appears in the detail field still resolves to a friendly
// message. Unknown codes fall through to a generic instruction.

export type Riot3ErrorMapping = {
  // One-line action the operator can take RIGHT NOW. English so the
  // banner doesn't depend on i18n infra.
  action: string;
};

const ERROR_MAP: Record<string, Riot3ErrorMapping> = {
  // x135 — robot brake error (most common)
  E230001: { action: "Robot brake error — recover the brake on the robot manually" },
  E750001: { action: "Robot brake error — recover the brake on the robot manually" },

  // x128 — robot in manual control
  E230002: { action: "Robot is in manual control mode — switch back to auto on the robot's panel" },
  E750002: { action: "Robot is in manual control mode — switch back to auto on the robot's panel" },

  // x46 — localization not started
  E230003: { action: "Robot localization not started — re-initiate localization on the robot" },
  E750004: { action: "Robot localization not started — re-initiate localization on the robot" },

  // x46 — E-stop triggered
  E230004: { action: "Robot E-stop triggered — release the E-stop button on the robot" },
  E750003: { action: "Robot E-stop triggered — release the E-stop button on the robot" },

  // x37 — robot offline
  E230006: { action: "Robot offline — check robot network connection and power state" },
  E750005: { action: "Robot offline — check robot network connection and power state" },

  // x44 — robot paused
  E230013: { action: "Robot is paused — inspect robot status on the floor" },
  E750022: { action: "Robot is paused — inspect robot status on the floor" },

  // x1 — coordinate mismatch
  E230023: { action: "AGV coordinate mismatch with task target — verify robot's actual position against the map" },
  E750036: { action: "AGV coordinate mismatch with task target — verify robot's actual position against the map" },

  // x36 — operation mode changed mid-mission (trip 500 case)
  E230025: { action: "Robot operation mode changed mid-mission — switch back to auto, then wait for RIOT to re-dispatch the mission" },

  // x30 — robot response timeout
  E112045: { action: "Robot response timeout — check connection, retry the command" },
  E750014: { action: "Robot response timeout — check connection, retry the command" },

  // x1 — manual operator suspend
  E112061: { action: "Manually suspended by an operator — click Resume to continue execution" },

  // x4 — path unreachable
  E107002: { action: "Path unreachable — check the map and clear any obstacles along the route" },
  E750000: { action: "Path unreachable — check the map and clear any obstacles along the route" },

  // x1 — server CPU > 80% (not trip-related)
  E610944: { action: "Server CPU usage above 80% — notify IT team (not trip-related)" },

  // NoVendorRecord — surfaced from /orders/.../operation responses
  // (HTTP 404 OR body code "E110014"). Already pre-empted by the
  // ResumeTripCommandHandler's auto-reconcile path, but keep the
  // mapping here so any other display surface (admin tools, debug
  // panel) shows the same instruction.
  E110014: { action: "RIOT no longer has this order — reopen the delivery order, then retry" },
};

/**
 * Resolve a RIOT3 error code to a human-readable action message.
 * Returns `null` when the code is unknown so callers can decide how to
 * fall back (typically: render the raw code + vendor message and prompt
 * the operator to escalate to the tech team).
 */
export function resolveRiot3ErrorAction(code: string | null | undefined): string | null {
  if (!code) return null;
  const trimmed = code.trim().toUpperCase();
  return ERROR_MAP[trimmed]?.action ?? null;
}
