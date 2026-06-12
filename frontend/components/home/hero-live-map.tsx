"use client";

import { useEffect, useMemo, useState } from "react";

import { CanvasCard } from "@/components/facility/maps-experience";
import {
  getStations,
  listMaps,
  type MapSummaryDto,
  type StationDto,
} from "@/lib/api/facility";

/* -------------------------------------------------------------------------- */
/* HeroLiveMap — the /home variant of the facility cartography.               */
/* Thin data-loader wrapper around the shared CanvasCard exported from        */
/* MapsExperience, so the home dashboard's map is the EXACT same renderer as  */
/* /facility/maps (same gestures, zoom controls, robot layer, pinned station  */
/* card) — just without the edit drawer / sync controls / station directory.  */
/* Auto-selects the first available map; navigate to /facility/maps to pick.  */
/* -------------------------------------------------------------------------- */
export function HeroLiveMap() {
  const [maps, setMaps] = useState<MapSummaryDto[]>([]);
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [stations, setStations] = useState<StationDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await listMaps();
        if (cancelled) return;
        setMaps(list);
        if (list.length > 0) setSelectedMapId(list[0]!.id);
        else setLoading(false);
      } catch {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!selectedMapId) return;
    let cancelled = false;
    setLoading(true);
    (async () => {
      try {
        const data = await getStations({ includeInactive: true, mapId: selectedMapId });
        if (cancelled) return;
        setStations(data);
      } catch {
        // Swallow — CanvasCard handles the empty-state UI on its own.
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedMapId]);

  const selectedMap = useMemo(
    () => maps.find((m) => m.id === selectedMapId) ?? null,
    [maps, selectedMapId],
  );

  return (
    <CanvasCard
      map={selectedMap}
      stations={stations}
      loading={loading}
      className="flex h-full flex-col"
      canvasClassName="flex-1 min-h-0"
    />
  );
}
