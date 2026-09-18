// Client for the carrier-type catalogue, which describes *kinds* of carrier.
// The individual physical racks and carts live in lib/api/fleet-carriers.ts.
//   GET    /api/v1/fleet/carrier-types → CarrierTypeDto[]
//   POST   /api/v1/fleet/carrier-types
//   GET    /api/v1/fleet/carrier-types/{code}
//   PUT    /api/v1/fleet/carrier-types/{code}
//   DELETE /api/v1/fleet/carrier-types/{code}
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
  /** The newest photo's id, for attachmentThumbnailUrl. An id rather than a
   *  URL so the address stays stable and the browser can cache it. */
  coverAttachmentId: string | null;
  photoCount: number;
  /** Carriers holding this type. Non-zero means delete will be refused, so the
   *  button can say so before the click instead of after the 409. */
  carrierCount: number;
};

// A refusal from the delete guard arrives as a 409 carrying the domain's own
// reason ("used by 4 carrier(s)"), so the message is surfaced verbatim — it is
// what tells the user which carriers to move first.
async function readError(res: Response): Promise<string> {
  try {
    const body = (await res.json()) as { message?: string } | string;
    if (typeof body === "string" && body.trim()) return body;
    if (typeof body === "object" && body?.message) return body.message;
  } catch {
    // not JSON
  }
  return `Request failed (${res.status})`;
}

const BASE = "/api/fleet/carrier-types";
const enc = (code: string) => encodeURIComponent(code);

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) throw new Error(await readError(res));
  return res.json();
}

export function getCarrierTypeProfiles(): Promise<CarrierTypeProfile[]> {
  return getJson<CarrierTypeProfile[]>(BASE);
}

export function getCarrierTypeProfile(code: string): Promise<CarrierTypeProfile> {
  return getJson<CarrierTypeProfile>(`${BASE}/${enc(code)}`);
}

async function send<T>(
  url: string,
  method: "POST" | "PUT" | "DELETE",
  body?: unknown,
): Promise<T | void> {
  const res = await fetch(url, {
    method,
    ...(body === undefined
      ? {}
      : { headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
  });
  if (!res.ok) throw new Error(await readError(res));
  // PUT and DELETE answer 204 — nothing to parse.
  if (res.status === 204) return;
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

/** The code is absent: it addresses the row and is immutable, the same rule
 *  carrier codes follow (ADR-019). */
export type UpdateCarrierTypeProfileInput = Omit<CreateCarrierTypeProfileInput, "code">;

export function createCarrierTypeProfile(
  input: CreateCarrierTypeProfileInput,
): Promise<string> {
  return send<string>(BASE, "POST", input) as Promise<string>;
}

export function updateCarrierTypeProfile(
  code: string,
  input: UpdateCarrierTypeProfileInput,
): Promise<void> {
  return send(`${BASE}/${enc(code)}`, "PUT", input) as Promise<void>;
}

export function deleteCarrierTypeProfile(code: string): Promise<void> {
  return send(`${BASE}/${enc(code)}`, "DELETE") as Promise<void>;
}

