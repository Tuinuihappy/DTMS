"use client";

// Phase 4.5 — Browser geolocation. Wrapped because the standard API
// returns a callback-style PositionError that's awkward to consume from
// async handlers; this turns it into a Promise + maps PERMISSION_DENIED
// into a user-actionable message.
export type GeoFix = { lat: number; lng: number; accuracy: number };

export async function getCurrentPosition(): Promise<GeoFix> {
  if (typeof navigator === "undefined" || !navigator.geolocation) {
    throw new Error("This device doesn't support GPS.");
  }
  return new Promise<GeoFix>((resolve, reject) => {
    navigator.geolocation.getCurrentPosition(
      (pos) =>
        resolve({
          lat: pos.coords.latitude,
          lng: pos.coords.longitude,
          accuracy: pos.coords.accuracy,
        }),
      (err) => {
        if (err.code === err.PERMISSION_DENIED) {
          reject(new Error("Location permission denied. Enable it in your browser settings."));
        } else if (err.code === err.POSITION_UNAVAILABLE) {
          reject(new Error("Couldn't fix your location. Move to an open area and try again."));
        } else if (err.code === err.TIMEOUT) {
          reject(new Error("Location request timed out."));
        } else {
          reject(new Error(err.message || "Location request failed."));
        }
      },
      // Higher accuracy + reasonable timeout. The geofence check
      // server-side allows for warehouse radii on the order of tens
      // of meters, so a too-coarse fix would fail spuriously.
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 30_000 },
    );
  });
}
