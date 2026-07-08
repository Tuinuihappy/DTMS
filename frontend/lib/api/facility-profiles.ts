// Client for Facility read endpoints not covered by lib/api/facility.ts:
//   GET /api/v1/facility/carrier-type-profiles          → CarrierTypeProfileDto[]
//   GET /api/v1/facility/load-unit-profiles?carrierType → LoadUnitProfileDto[]
//   GET /api/v1/facility/route-cost?from&to             → RouteCostDto

// NB: the backend property "AMRCapability" serializes to camelCase as
// "aMRCapability" (System.Text.Json lowercases only the first character).
export type CarrierTypeProfile = {
  id: string;
  code: string;
  displayName: string;
  aMRCapability: string;
  maxWeightKg: number | null;
  maxSlots: number | null;
  description: string | null;
};

export type LoadUnitProfile = {
  id: string;
  code: string;
  displayName: string;
  lengthMm: number;
  widthMm: number;
  heightMm: number;
  maxGrossWeightKg: number;
  carrierTypeCode: string;
};

export type RouteCost = {
  fromStationId: string;
  toStationId: string;
  cost: number;
  distanceMm: number;
};

async function getJson<T>(url: string, notFoundOk = false): Promise<T> {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) {
    if (notFoundOk && res.status === 404) {
      // route-cost returns 404 when no edge exists — surface a typed error
      throw new Error("No route found between the selected stations.");
    }
    let message = `Request failed (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // not JSON
    }
    throw new Error(message);
  }
  return res.json();
}

export function getCarrierTypeProfiles(): Promise<CarrierTypeProfile[]> {
  return getJson<CarrierTypeProfile[]>("/api/facility/carrier-type-profiles");
}

export function getLoadUnitProfiles(carrierTypeCode?: string): Promise<LoadUnitProfile[]> {
  const qs = carrierTypeCode ? `?carrierTypeCode=${encodeURIComponent(carrierTypeCode)}` : "";
  return getJson<LoadUnitProfile[]>(`/api/facility/load-unit-profiles${qs}`);
}

export function getRouteCost(from: string, to: string): Promise<RouteCost> {
  const qs = new URLSearchParams({ from, to });
  return getJson<RouteCost>(`/api/facility/route-cost?${qs.toString()}`, true);
}
