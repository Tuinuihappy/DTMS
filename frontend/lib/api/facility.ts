export type StationDto = {
  id: string;
  mapId: string;
  name: string;
  type: string;
  x: number;
  y: number;
  theta: number | null;
  vendorRef: string | null;
  code: string | null;
  isActive: boolean;
  zoneId: string | null;
  compatibleVehicleTypes: string[];
  manualOverrideOffline: boolean;
  isManualOverrideActive: boolean;
  // Optional manual-override metadata — present when the operator force-offlined
  // this station. Used by the edit drawer's "currently offline" panel.
  manualOverrideReason?: string | null;
  manualOverrideAt?: string | null;
  manualOverrideBy?: string | null;
  manualOverrideExpiresAt?: string | null;
};

export type StationOption = {
  code: string;
  name: string;
  type: string;
};

export type MapSummaryDto = {
  id: string;
  name: string;
  version: string;
  width: number;
  height: number;
  vendorRef: string | null;
  stationCount: number;
  activeStationCount: number;
};

export type SyncMapStationsResultDto = {
  mapId: string;
  mapName: string;
  added: number;
  updated: number;
  reactivated: number;
  deactivated: number;
};

export async function getStations(opts?: {
  includeInactive?: boolean;
  mapId?: string;
}): Promise<StationDto[]> {
  const qs = new URLSearchParams({
    includeInactive: opts?.includeInactive ? "true" : "false",
  });
  if (opts?.mapId) qs.set("mapId", opts.mapId);
  const res = await fetch(`/api/facility/stations?${qs.toString()}`, {
    cache: "no-store",
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load stations (${res.status})`);
  }
  return res.json();
}

// Stations suitable for pickup/drop dropdowns — active, not force-offline,
// has a non-null code. Sorted by code for predictable order.
export async function getStationOptions(): Promise<StationOption[]> {
  const stations = await getStations();
  return stations
    .filter((s) => s.isActive && !s.isManualOverrideActive && s.code)
    .map((s) => ({ code: s.code as string, name: s.name, type: s.type }))
    .sort((a, b) => a.code.localeCompare(b.code));
}

// React hook — fetches station options once on mount, then exposes the
// list + loading + error to consumers. Multiple comboboxes on one page
// can share a single hook instance via prop drilling so the API isn't
// hit more than once per render.
import { useEffect, useState } from "react";

export type UseStationOptionsResult = {
  stations: StationOption[];
  loading: boolean;
  error: string | null;
};

export function useStationOptions(): UseStationOptionsResult {
  const [stations, setStations] = useState<StationOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getStationOptions()
      .then((opts) => {
        if (!cancelled) setStations(opts);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message || "Failed to load stations");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return { stations, loading, error };
}

export type RobotPositionDto = {
  deviceKey: string;
  deviceName: string;
  mapId: string;
  vendorMapId: number;
  x: number;
  y: number;
  theta: number;
  systemState: string;
  connectionState: string;
  emergency: boolean;
  paused: boolean;
  batteryPercentage: number;
  charging: boolean;
  orderKey: string | null;
  orderName: string | null;
  startToEnd: string | null;
  stationName: string | null;
  updatedAtUtc: string;
};

export async function getMapRobotPositions(
  mapId: string,
  signal?: AbortSignal,
): Promise<RobotPositionDto[]> {
  const res = await fetch(`/api/facility/maps/${mapId}/robot-positions`, {
    cache: "no-store",
    signal,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load robot positions (${res.status})`);
  }
  return res.json();
}

export async function listMaps(): Promise<MapSummaryDto[]> {
  const res = await fetch("/api/facility/maps", { cache: "no-store" });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load maps (${res.status})`);
  }
  return res.json();
}

export type StationTypeWire =
  | "NORMAL"
  | "CHARGING"
  | "PICKUP"
  | "DROPOFF"
  | "PARKING"
  | "DOCK"
  | "CHECKPOINT";

export type UpdateStationInput = {
  type?: StationTypeWire;
  code?: string | null;
};

export async function updateStation(
  stationId: string,
  input: UpdateStationInput,
): Promise<void> {
  const res = await fetch(`/api/facility/stations/${stationId}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
  if (!res.ok) {
    let message = `Update failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // body wasn't JSON
    }
    throw new Error(message);
  }
}

export type ForceOfflineInput = {
  reason: string;
  durationMinutes: number;
  by?: string | null;
};

export async function forceStationOffline(
  stationId: string,
  input: ForceOfflineInput,
): Promise<void> {
  const res = await fetch(`/api/facility/stations/${stationId}/force-offline`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
  if (!res.ok) {
    let message = `Force-offline failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // body wasn't JSON
    }
    throw new Error(message);
  }
}

export async function clearStationOverride(stationId: string): Promise<void> {
  const res = await fetch(`/api/facility/stations/${stationId}/force-offline`, {
    method: "DELETE",
  });
  if (!res.ok) {
    let message = `Clear-override failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // body wasn't JSON
    }
    throw new Error(message);
  }
}

export async function syncMapStations(mapId: string): Promise<SyncMapStationsResultDto> {
  const res = await fetch(`/api/facility/maps/${mapId}/sync-stations`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
  if (!res.ok) {
    let message = `Sync failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // body wasn't JSON — fall through to default message
    }
    throw new Error(message);
  }
  return res.json();
}
