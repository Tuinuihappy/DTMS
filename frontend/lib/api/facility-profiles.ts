// Client for Facility read endpoints not covered by lib/api/facility.ts:
//   GET /api/v1/facility/carrier-type-profiles → CarrierTypeProfileDto[]
//
// The load-unit-profile catalogue was removed in carrier-tracking P0.1 — it
// had no consumer outside its own CRUD. Item.loadUnitProfileCode survives as
// free text on the order payload; it was never validated against a catalogue.

// The backend property was renamed AMRCapability → AmrCapability when the
// catalogue moved to Fleet (ADR-019), so the wire name is now a clean
// "amrCapability" instead of the old "aMRCapability" that System.Text.Json
// produced by lowercasing only the first character.
export type CarrierTypeProfile = {
  id: string;
  code: string;
  displayName: string;
  amrCapability: string;
  maxWeightKg: number | null;
  maxSlots: number | null;
  description: string | null;
};

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) {
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

async function postJson<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const b = (await res.json()) as { message?: string };
      if (b?.message) message = b.message;
    } catch {
      // not JSON
    }
    throw new Error(message);
  }
  return res.json();
}

export type CreateCarrierTypeProfileInput = {
  code: string;
  displayName: string;
  amrCapability: string;
  maxWeightKg?: number | null;
  maxSlots?: number | null;
  description?: string | null;
};

export function createCarrierTypeProfile(
  input: CreateCarrierTypeProfileInput,
): Promise<string> {
  return postJson<string>("/api/facility/carrier-type-profiles", input);
}

