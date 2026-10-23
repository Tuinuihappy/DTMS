// Shared between the server shell layout and the client LeftRail — kept
// out of the "use client" module so the server gets the value, not a
// client reference.

/** "1" when the desktop rail is expanded. Read on the server so the first
 *  render already has the right width. */
export const RAIL_COOKIE = "tms-rail-expanded";

export const RAIL_COLLAPSED_PX = 56;
export const RAIL_EXPANDED_PX = 232;
// Total horizontal space the rail consumes on the page (rail width + the
// fixed left-4 inset + an 8 px breathing buffer). Every <main> reads this
// from `--rail-width` for its left padding.
export const RAIL_COLLAPSED_LANE = `${RAIL_COLLAPSED_PX + 16 + 8}px`; // 80 px
export const RAIL_EXPANDED_LANE = `${RAIL_EXPANDED_PX + 16 + 0}px`; // 248 px
