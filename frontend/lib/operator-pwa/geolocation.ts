"use client";

// Phase 4.5 — Browser geolocation. Wrapped because the standard API
// returns a callback-style PositionError that's awkward to consume from
// async handlers; this turns it into a Promise + maps PERMISSION_DENIED
// into a user-actionable message.
export type GeoFix = { lat: number; lng: number; accuracy: number };

// Dev-only escape hatch for testing on tablets over HTTP LAN IPs, where
// Chromium refuses geolocation regardless of permission state. Two ways
// to activate, checked in this order:
//   1. localStorage["dtms.mockGps"] = "lat,lng"  — per-device, no rebuild
//      Also activated by visiting any URL with ?mockGps=lat,lng once.
//   2. NEXT_PUBLIC_OPERATOR_MOCK_GPS_LAT/_LNG    — baked at build time
function readMockFix(): GeoFix | null {
  if (typeof window !== "undefined") {
    const params = new URLSearchParams(window.location.search);
    const fromUrl = params.get("mockGps");
    if (fromUrl) {
      window.localStorage.setItem("dtms.mockGps", fromUrl);
    }
    const stored = window.localStorage.getItem("dtms.mockGps");
    if (stored) {
      const [latStr, lngStr] = stored.split(",");
      const lat = Number(latStr);
      const lng = Number(lngStr);
      if (Number.isFinite(lat) && Number.isFinite(lng)) {
        return { lat, lng, accuracy: 10 };
      }
    }
  }
  const lat = Number(process.env.NEXT_PUBLIC_OPERATOR_MOCK_GPS_LAT);
  const lng = Number(process.env.NEXT_PUBLIC_OPERATOR_MOCK_GPS_LNG);
  if (!Number.isFinite(lat) || !Number.isFinite(lng)) return null;
  return { lat, lng, accuracy: 10 };
}

export async function getCurrentPosition(): Promise<GeoFix> {
  const mock = readMockFix();
  if (mock) {
    console.warn("[geolocation] using NEXT_PUBLIC_OPERATOR_MOCK_GPS fix", mock);
    return mock;
  }
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
