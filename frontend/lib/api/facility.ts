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
// Deduped by code: the combobox uses code as the React key, and duplicate
// keys corrupt list reconciliation (seen with orphaned stations of a
// deleted map). The backend filters those out too — this is insurance.
export async function getStationOptions(): Promise<StationOption[]> {
  const stations = await getStations();
  return dedupeByCode(
    stations
      .filter((s) => s.isActive && !s.isManualOverrideActive && s.code)
      .map((s) => ({ code: s.code as string, name: s.name, type: s.type })),
  ).sort((a, b) => a.code.localeCompare(b.code));
}

function dedupeByCode(options: StationOption[]): StationOption[] {
  const seen = new Set<string>();
  return options.filter((o) =>
    seen.has(o.code) ? false : (seen.add(o.code), true),
  );
}

// React hook — fetches station options once on mount, then exposes the
// list + loading + error to consumers. Multiple comboboxes on one page
// can share a single hook instance via prop drilling so the API isn't
// hit more than once per render.
import { useEffect, useState } from "react";

export type UseStationOptionsResult = {
  stations: StationOption[];
  // Station id (DTMS Guid) → code. Includes inactive stations so a
  // template that references a now-deactivated station still resolves
  // back to the saved code when the editor opens.
  byId: Map<string, string>;
  loading: boolean;
  error: string | null;
};

export function useStationOptions(): UseStationOptionsResult {
  const [stations, setStations] = useState<StationOption[]>([]);
  const [byId, setById] = useState<Map<string, string>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    // Fetch with includeInactive so the byId lookup covers
    // deactivated stations too; the dropdown options still filter to
    // active+non-override+has-code below.
    getStations({ includeInactive: true })
      .then((all) => {
        if (cancelled) return;
        const active = dedupeByCode(
          all
            .filter((s) => s.isActive && !s.isManualOverrideActive && s.code)
            .map((s) => ({ code: s.code as string, name: s.name, type: s.type })),
        ).sort((a, b) => a.code.localeCompare(b.code));
        const map = new Map<string, string>();
        for (const s of all) if (s.code) map.set(s.id, s.code);
        setStations(active);
        setById(map);
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

  return { stations, byId, loading, error };
}

// Stations exposed by their RIOT3 numeric vendor id — the int the MOVE
// mission payload expects. Skips stations without a parseable VendorRef
// (legacy rows or maps not yet synced), and active+not-force-offline so
// templates can't reference a station the operator has flagged offline.
export type StationVendorOption = {
  vendorId: number;
  name: string;
  type: string;
  code: string | null;
};

export async function getStationVendorOptions(): Promise<StationVendorOption[]> {
  const stations = await getStations();
  const out: StationVendorOption[] = [];
  for (const s of stations) {
    if (!s.isActive || s.isManualOverrideActive) continue;
    if (!s.vendorRef) continue;
    const n = Number(s.vendorRef);
    if (!Number.isInteger(n)) continue;
    out.push({ vendorId: n, name: s.name, type: s.type, code: s.code });
  }
  return out.sort((a, b) => a.vendorId - b.vendorId);
}

export type UseStationVendorOptionsResult = {
  stations: StationVendorOption[];
  loading: boolean;
  error: string | null;
};

export function useStationVendorOptions(): UseStationVendorOptionsResult {
  const [stations, setStations] = useState<StationVendorOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getStationVendorOptions()
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

// Maps exposed by their RIOT3 numeric vendor id — the int the MOVE
// mission payload expects. Skips maps without a parseable VendorRef
// (e.g. locally-created maps that were never synced from RIOT3).
export type MapVendorOption = {
  vendorId: number;
  name: string;
  version: string;
};

export async function getMapVendorOptions(): Promise<MapVendorOption[]> {
  const maps = await listMaps();
  const out: MapVendorOption[] = [];
  for (const m of maps) {
    if (!m.vendorRef) continue;
    const n = Number(m.vendorRef);
    if (!Number.isInteger(n)) continue;
    out.push({ vendorId: n, name: m.name, version: m.version });
  }
  return out.sort((a, b) => a.vendorId - b.vendorId);
}

export type UseMapVendorOptionsResult = {
  maps: MapVendorOption[];
  loading: boolean;
  error: string | null;
};

export function useMapVendorOptions(): UseMapVendorOptionsResult {
  const [maps, setMaps] = useState<MapVendorOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getMapVendorOptions()
      .then((opts) => {
        if (!cancelled) setMaps(opts);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message || "Failed to load maps");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return { maps, loading, error };
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
