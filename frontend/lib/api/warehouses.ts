// Browser-side fetch helpers + DTO types for the Warehouse module
// (added in Phase 2.1-2.7a). Talks to the Next.js proxy under
// /api/facility/warehouses which forwards to /api/v1/facility/warehouses
// — same proxy pattern as stations/maps. Per ADR-002 every order
// references a warehouse (building / site); AMR additionally references
// a specific station inside it.

import { useEffect, useState } from "react";

export type TransportModeWire = "Amr" | "Manual" | "Fleet";

// List view DTO — flat shape, omits the full geofence WKT (some
// polygons are 5KB) and the per-day operating hours. Picker UIs only
// need code/name/serviceModes; the detail endpoint covers everything
// else when actually editing.
export type WarehouseListItemDto = {
  id: string;
  code: string;
  name: string;
  lat: number;
  lng: number;
  addressStreet: string;
  addressCity: string | null;
  serviceModes: TransportModeWire[];
  geofenceRadiusM: number | null;
  hasGeofencePolygon: boolean;
  contactName: string | null;
  contactPhone: string | null;
  isActive: boolean;
  createdAt: string;
};

// Full detail — includes WKT, contact email, and per-day open/close
// strings. Used by edit pages + the Phase 4 geofence editor.
export type WarehouseDetailDto = {
  id: string;
  code: string;
  name: string;
  lat: number;
  lng: number;
  addressStreet: string;
  addressCity: string | null;
  addressState: string | null;
  addressPostalCode: string | null;
  addressCountry: string | null;
  serviceModes: TransportModeWire[];
  geofenceRadiusM: number | null;
  geofenceAreaWkt: string | null;
  contactName: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
  monday: OperatingHoursDay;
  tuesday: OperatingHoursDay;
  wednesday: OperatingHoursDay;
  thursday: OperatingHoursDay;
  friday: OperatingHoursDay;
  saturday: OperatingHoursDay;
  sunday: OperatingHoursDay;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
};

// "HH:mm" strings or null (closed that day). Matches the backend DTO
// shape — TimeOnly serialized via OperatingHours.cs ToString("hh\\:mm").
export type OperatingHoursDay = {
  open: string | null;
  close: string | null;
};

// Picker option — just what the dropdown needs. Mirrors StationOption
// shape so callers can build similar list/dropdown UIs.
export type WarehouseOption = {
  id: string;
  code: string;
  name: string;
  serviceModes: TransportModeWire[];
  isActive: boolean;
};

// ── List query ───────────────────────────────────────────────────────────

export async function getWarehouses(opts?: {
  serviceMode?: TransportModeWire;
  // Defaults to true on the backend; pass false to include soft-deleted
  // for admin / audit pages.
  excludeInactive?: boolean;
}): Promise<WarehouseListItemDto[]> {
  const qs = new URLSearchParams();
  if (opts?.serviceMode) qs.set("serviceMode", opts.serviceMode);
  if (opts?.excludeInactive === false) qs.set("excludeInactive", "false");
  const path = qs.toString()
    ? `/api/facility/warehouses?${qs.toString()}`
    : "/api/facility/warehouses";
  const res = await fetch(path, { cache: "no-store" });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load warehouses (${res.status})`);
  }
  return res.json();
}

// Filtered-and-sorted picker source. Mirrors getStationOptions —
// only active warehouses, sorted by code for predictable order.
export async function getWarehouseOptions(opts?: {
  serviceMode?: TransportModeWire;
}): Promise<WarehouseOption[]> {
  const warehouses = await getWarehouses({
    serviceMode: opts?.serviceMode,
    excludeInactive: true,
  });
  return warehouses
    .map((w) => ({
      id: w.id,
      code: w.code,
      name: w.name,
      serviceModes: w.serviceModes,
      isActive: w.isActive,
    }))
    .sort((a, b) => a.code.localeCompare(b.code));
}

// ── Detail / single fetch ────────────────────────────────────────────────

export async function getWarehouseById(id: string): Promise<WarehouseDetailDto> {
  const res = await fetch(`/api/facility/warehouses/${id}`, { cache: "no-store" });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load warehouse (${res.status})`);
  }
  return res.json();
}

// ── Mutations ────────────────────────────────────────────────────────────

export type CreateWarehouseInput = {
  code: string;
  name: string;
  lat: number;
  lng: number;
  addressStreet: string;
  addressCity?: string | null;
  addressState?: string | null;
  addressPostalCode?: string | null;
  addressCountry?: string | null;
  serviceModes?: TransportModeWire[] | null;
  contactName?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  geofenceRadiusM?: number | null;
  geofenceAreaWkt?: string | null;
};

export async function createWarehouse(
  input: CreateWarehouseInput,
): Promise<{ id: string }> {
  const res = await fetch("/api/facility/warehouses", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
  if (!res.ok) {
    throw await extractApiError(res, "Failed to create warehouse");
  }
  return res.json();
}

// Partial update — server treats missing fields as "don't change".
// Use null on a nullable field (e.g. contactEmail) to explicitly null
// the column; for the contact pair, send empty strings to clear the
// whole contact (server's partial-update semantics).
export type UpdateWarehouseInput = {
  name?: string;
  lat?: number;
  lng?: number;
  addressStreet?: string;
  addressCity?: string | null;
  addressState?: string | null;
  addressPostalCode?: string | null;
  addressCountry?: string | null;
  serviceModes?: TransportModeWire[];
  contactName?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  geofenceRadiusM?: number | null;
  geofenceAreaWkt?: string | null;
  // Explicit "remove geofence" signal — distinguishes "leave as-is"
  // from "wipe it". Matches the backend command shape.
  clearGeofence?: boolean;
};

export async function updateWarehouse(
  id: string,
  input: UpdateWarehouseInput,
): Promise<void> {
  const res = await fetch(`/api/facility/warehouses/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
  });
  if (!res.ok) {
    throw await extractApiError(res, "Failed to update warehouse");
  }
}

export async function deactivateWarehouse(id: string): Promise<void> {
  const res = await fetch(`/api/facility/warehouses/${id}/deactivate`, {
    method: "POST",
  });
  if (!res.ok) {
    throw await extractApiError(res, "Failed to deactivate warehouse");
  }
}

export async function reactivateWarehouse(id: string): Promise<void> {
  const res = await fetch(`/api/facility/warehouses/${id}/reactivate`, {
    method: "POST",
  });
  if (!res.ok) {
    throw await extractApiError(res, "Failed to reactivate warehouse");
  }
}

// ── Helpers ──────────────────────────────────────────────────────────────

// Backend returns errors as { error: "message" } via the proxy + the
// passthrough-errors normalization. Try to surface that field; fall
// back to generic message.
async function extractApiError(res: Response, fallback: string): Promise<Error> {
  let message = `${fallback} (${res.status})`;
  try {
    const body = (await res.json()) as { error?: string; message?: string };
    if (body?.error) message = body.error;
    else if (body?.message) message = body.message;
  } catch {
    // body wasn't JSON
  }
  return new Error(message);
}

// ── React hooks ──────────────────────────────────────────────────────────

export type UseWarehouseOptionsResult = {
  warehouses: WarehouseOption[];
  // Id → code lookup for back-population in edit forms (where the
  // saved record carries an id but the picker is indexed by code).
  byId: Map<string, string>;
  loading: boolean;
  error: string | null;
};

// Mirrors useStationOptions — single fetch on mount, suitable for
// multiple comboboxes on one page when prop-drilled.
export function useWarehouseOptions(opts?: {
  serviceMode?: TransportModeWire;
}): UseWarehouseOptionsResult {
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);
  const [byId, setById] = useState<Map<string, string>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Capture the filter primitive — the object reference would re-trigger
  // every render even if the user didn't change anything.
  const serviceMode = opts?.serviceMode;

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getWarehouseOptions({ serviceMode })
      .then((opts) => {
        if (cancelled) return;
        setWarehouses(opts);
        const map = new Map<string, string>();
        for (const w of opts) map.set(w.id, w.code);
        setById(map);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message || "Failed to load warehouses");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [serviceMode]);

  return { warehouses, byId, loading, error };
}
