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

export async function listMaps(): Promise<MapSummaryDto[]> {
  const res = await fetch("/api/facility/maps", { cache: "no-store" });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load maps (${res.status})`);
  }
  return res.json();
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
