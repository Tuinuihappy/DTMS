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

export async function getStations(opts?: { includeInactive?: boolean }): Promise<StationDto[]> {
  // Backend treats includeInactive as a non-nullable bool, so it must be
  // present in the query string even when false.
  const qs = new URLSearchParams({
    includeInactive: opts?.includeInactive ? "true" : "false",
  });
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
