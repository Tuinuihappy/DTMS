// WMS PR-4 — client-side fetch helpers for the WMS location picker.
// Server-side search + pagination because the WMS catalogue can grow
// to thousands of locations; the create-order dialog picker fires
// debounced queries as the user types.

import { useCallback, useEffect, useRef, useState } from "react";

export type WmsLocationSummaryDto = {
  id: string;                            // Guid — internal WMS location id
  externalId: number;                    // int — upstream WMS id (audit only)
  code: string;                          // e.g. "STF_35" — what Order.PickupLocationCode stores
  displayName: string;
  type: number;                          // WMS type code (5 = WIP, 2 = Production, 99 = Other)
  typeName: string | null;               // "WIP" / "Production" / …
  isActive: boolean;
  parentLocationCode: string | null;     // zone key ("LOC-000149" = WIP zone)
  parentLocationDisplayName: string | null;
  latitude: number | null;
  longitude: number | null;
};

export type WmsLocationsPage = {
  total: number;
  page: number;
  pageSize: number;
  data: WmsLocationSummaryDto[];
};

export type SearchWmsLocationsOptions = {
  search?: string;
  parentCode?: string;
  page?: number;
  pageSize?: number;
  includeInactive?: boolean;
  signal?: AbortSignal;
};

export async function searchWmsLocations(
  opts: SearchWmsLocationsOptions = {},
): Promise<WmsLocationsPage> {
  const qs = new URLSearchParams();
  if (opts.search) qs.set("search", opts.search);
  if (opts.parentCode) qs.set("parentCode", opts.parentCode);
  if (opts.page) qs.set("page", String(opts.page));
  if (opts.pageSize) qs.set("pageSize", String(opts.pageSize));
  if (opts.includeInactive) qs.set("includeInactive", "true");

  const url = qs.toString()
    ? `/api/wms/locations?${qs.toString()}`
    : "/api/wms/locations";
  const res = await fetch(url, { cache: "no-store", signal: opts.signal });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to search WMS locations (${res.status})`);
  }
  return res.json();
}

// ── React hook ──────────────────────────────────────────────────────────

export type UseWmsLocationSearchResult = {
  items: WmsLocationSummaryDto[];
  total: number;
  loading: boolean;
  error: string | null;
};

/**
 * Debounced server-side search hook for the picker. Fires a new fetch
 * `debounceMs` after the last keystroke; any in-flight request is
 * aborted when the query changes so we don't race on a stale response.
 */
export function useWmsLocationSearch(
  query: string,
  opts: { pageSize?: number; debounceMs?: number } = {},
): UseWmsLocationSearchResult {
  const pageSize = opts.pageSize ?? 20;
  const debounceMs = opts.debounceMs ?? 300;

  const [items, setItems] = useState<WmsLocationSummaryDto[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const controllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    // Cancel any in-flight request before scheduling the next one.
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;

    const timer = setTimeout(async () => {
      setLoading(true);
      setError(null);
      try {
        const page = await searchWmsLocations({
          search: query.trim() || undefined,
          page: 1,
          pageSize,
          signal: controller.signal,
        });
        if (!controller.signal.aborted) {
          setItems(page.data);
          setTotal(page.total);
        }
      } catch (e) {
        if ((e as Error).name === "AbortError") return;
        setError((e as Error).message || "Failed to search WMS locations");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }, debounceMs);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query, pageSize, debounceMs]);

  return { items, total, loading, error };
}

// ── Detail lookup by code (for back-population in edit forms) ────────────

/**
 * Fetch a single WMS location by its code. Used when opening an existing
 * order for edit — the saved order stores the code, and the picker needs
 * the full row to render the initial "current selection" chip.
 */
export async function getWmsLocationByCode(
  code: string,
): Promise<WmsLocationSummaryDto | null> {
  if (!code.trim()) return null;
  const page = await searchWmsLocations({
    search: code,
    pageSize: 5,
  });
  // Backend does ILike matching; find the exact-code row (case-insensitive).
  return (
    page.data.find((l) => l.code.toLowerCase() === code.toLowerCase()) ?? null
  );
}
