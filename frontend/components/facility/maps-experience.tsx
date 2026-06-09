"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AnimatePresence, motion } from "motion/react";
import {
  Activity,
  AlertTriangle,
  ArrowUpRight,
  BatteryCharging,
  CheckCircle2,
  Compass,
  Crosshair,
  Hexagon,
  Layers,
  Loader2,
  Map as MapIcon,
  MapPin,
  Maximize2,
  Navigation,
  PackageOpen,
  ParkingCircle,
  RefreshCw,
  Search,
  Signal,
  Sparkles,
  X,
  Zap,
} from "lucide-react";

import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { SectionLabel } from "@/components/primitives/section-label";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";
import {
  getStations,
  listMaps,
  syncMapStations,
  type MapSummaryDto,
  type StationDto,
  type SyncMapStationsResultDto,
} from "@/lib/api/facility";

/* -------------------------------------------------------------------------- */
/* Station-type lexicon                                                       */
/* The backend serialises StationType as SCREAMING_SNAKE (NORMAL, PICKUP, …). */
/* Everything below keys off that wire token, normalised through `typeKey`.   */
/* -------------------------------------------------------------------------- */
type TypeKey =
  | "NORMAL"
  | "CHARGING"
  | "PICKUP"
  | "DROPOFF"
  | "PARKING"
  | "DOCK"
  | "CHECKPOINT";

const TYPE_KEYS: TypeKey[] = [
  "NORMAL",
  "CHARGING",
  "PICKUP",
  "DROPOFF",
  "PARKING",
  "DOCK",
  "CHECKPOINT",
];

const TYPE_META: Record<
  TypeKey,
  {
    label: string;
    swatch: string; // raw color (var ref) for canvas / chips
    chip: string; // tailwind classes for pill chips
    icon: typeof MapPin;
  }
> = {
  NORMAL: {
    label: "Waypoint",
    swatch: "var(--color-brand-500)",
    chip: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
    icon: MapPin,
  },
  CHARGING: {
    label: "Charging",
    swatch: "var(--color-amber)",
    chip: "bg-[var(--color-amber-soft)] text-[#8a4a07] dark:text-[var(--color-amber)]",
    icon: BatteryCharging,
  },
  PICKUP: {
    label: "Pickup",
    swatch: "#e07248",
    chip: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
    icon: PackageOpen,
  },
  DROPOFF: {
    label: "Drop-off",
    swatch: "#16a37b",
    chip: "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
    icon: Layers,
  },
  PARKING: {
    label: "Parking",
    swatch: "#7c6acd",
    chip: "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]",
    icon: ParkingCircle,
  },
  DOCK: {
    label: "Dock",
    swatch: "#3b6bd6",
    chip: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
    icon: Hexagon,
  },
  CHECKPOINT: {
    label: "Checkpoint",
    swatch: "var(--color-ink-500)",
    chip: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
    icon: Crosshair,
  },
};

function typeKey(raw: string | undefined | null): TypeKey {
  const t = (raw ?? "NORMAL").toString().toUpperCase();
  return (TYPE_KEYS as string[]).includes(t) ? (t as TypeKey) : "NORMAL";
}

/* -------------------------------------------------------------------------- */
/* Time-ago — last-sync stamp without a heavy date lib.                       */
/* -------------------------------------------------------------------------- */
function useTimeAgo(date: Date | null): string {
  const [, force] = useState(0);
  useEffect(() => {
    if (!date) return;
    const t = window.setInterval(() => force((n) => (n + 1) % 1_000_000), 15_000);
    return () => window.clearInterval(t);
  }, [date]);
  if (!date) return "—";
  const seconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  const mins = Math.round(seconds / 60);
  if (mins < 60) return `${mins} min ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs} hr ago`;
  return `${Math.round(hrs / 24)} d ago`;
}

/* -------------------------------------------------------------------------- */
/* MapsExperience — orchestrator                                              */
/* Owns the data graph: list of maps, the currently-selected map, the         */
/* stations on that map, and the sync state machine. Sub-components are       */
/* pure renderers driven by callbacks.                                        */
/* -------------------------------------------------------------------------- */
export function MapsExperience() {
  const [maps, setMaps] = useState<MapSummaryDto[]>([]);
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [stations, setStations] = useState<StationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [lastSync, setLastSync] = useState<Date | null>(null);
  const [lastSyncResult, setLastSyncResult] = useState<SyncMapStationsResultDto | null>(
    null,
  );
  const [toast, setToast] = useState<{
    kind: "ok" | "err";
    msg: string;
  } | null>(null);

  // Initial maps fetch.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await listMaps();
        if (cancelled) return;
        setMaps(list);
        if (list.length > 0) setSelectedMapId(list[0]!.id);
        else setLoading(false);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Failed to load maps");
        setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Stations refetch when selected map changes.
  const refreshStations = useCallback(async (mapId: string) => {
    setLoading(true);
    try {
      const all = await getStations({ includeInactive: true, mapId });
      setStations(all);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load stations");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (selectedMapId) void refreshStations(selectedMapId);
  }, [selectedMapId, refreshStations]);

  // Auto-dismiss toast after 4s.
  useEffect(() => {
    if (!toast) return;
    const t = window.setTimeout(() => setToast(null), 4000);
    return () => window.clearTimeout(t);
  }, [toast]);

  const selectedMap = useMemo(
    () => maps.find((m) => m.id === selectedMapId) ?? null,
    [maps, selectedMapId],
  );

  const stats = useMemo(() => {
    const total = stations.length;
    const active = stations.filter((s) => s.isActive).length;
    const linked = stations.filter((s) => s.vendorRef).length;
    const offline = stations.filter((s) => s.isManualOverrideActive).length;
    return { total, active, linked, offline };
  }, [stations]);

  const onSync = useCallback(async () => {
    if (!selectedMapId || syncing) return;
    setSyncing(true);
    try {
      const result = await syncMapStations(selectedMapId);
      setLastSync(new Date());
      setLastSyncResult(result);
      const delta = result.added + result.updated + result.reactivated + result.deactivated;
      setToast({
        kind: "ok",
        msg:
          delta === 0
            ? `${result.mapName} is already up to date`
            : `Synced ${delta} station${delta === 1 ? "" : "s"} on ${result.mapName}`,
      });
      await refreshStations(selectedMapId);
    } catch (err) {
      setToast({
        kind: "err",
        msg: err instanceof Error ? err.message : "Sync failed",
      });
    } finally {
      setSyncing(false);
    }
  }, [selectedMapId, syncing, refreshStations]);

  return (
    <div className="space-y-6 md:space-y-7">
      <HeaderStrip
        mapName={selectedMap?.name ?? null}
        onSync={onSync}
        syncing={syncing}
        canSync={!!selectedMap?.vendorRef}
      />

      <KpiStrip stats={stats} lastSync={lastSync} loading={loading} />

      <div className="grid grid-cols-12 gap-5 md:gap-6">
        <div className="col-span-12 lg:col-span-8 space-y-5 md:space-y-6">
          <MapSelectorRail
            maps={maps}
            selectedId={selectedMapId}
            onSelect={setSelectedMapId}
            loading={loading && maps.length === 0}
          />
          <CanvasCard map={selectedMap} stations={stations} loading={loading} />
        </div>

        <div className="col-span-12 lg:col-span-4 space-y-5 md:space-y-6">
          <SyncCommandCard
            map={selectedMap}
            syncing={syncing}
            onSync={onSync}
            lastSync={lastSync}
            lastResult={lastSyncResult}
          />
          <FacilityInfoCard map={selectedMap} stats={stats} />
          <LegendCard stations={stations} />
        </div>
      </div>

      <StationDirectory stations={stations} loading={loading} error={error} />

      <Toast toast={toast} onDismiss={() => setToast(null)} />
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* HeaderStrip — page eyebrow, title, and the primary refresh CTA.            */
/* -------------------------------------------------------------------------- */
function HeaderStrip({
  mapName,
  onSync,
  syncing,
  canSync,
}: {
  mapName: string | null;
  onSync: () => void;
  syncing: boolean;
  canSync: boolean;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: -10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, ease: [0.22, 1, 0.36, 1] }}
      className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between"
    >
      <SectionLabel
        icon={<MapIcon className="h-4 w-4" strokeWidth={2} />}
        title="Site cartography"
        subtitle={
          mapName
            ? `Live station topology for ${mapName}. Refresh to pull the latest from RIOT3.`
            : "Live station topology, synced from RIOT3."
        }
      />

      <div className="flex items-center gap-2">
        <div className="hidden md:flex items-center gap-2 rounded-full glass px-3 py-1.5 text-[11.5px] font-medium tracking-tight text-[var(--color-ink-600)]">
          <StatusPulse tone="success" />
          <span>Topology live</span>
          <span className="text-[var(--color-ink-300)]">·</span>
          <span className="font-mono text-[10.5px] tabular-nums text-[var(--color-ink-500)]">
            v4.maps
          </span>
        </div>

        <button
          type="button"
          onClick={onSync}
          disabled={syncing || !canSync}
          className={cn(
            "group relative inline-flex items-center gap-2.5 rounded-full px-4 h-10 text-[12.5px] font-semibold tracking-tight transition-all duration-200",
            "bg-[var(--color-brand-900)] text-white",
            "shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_10px_24px_-10px_rgba(14,21,48,0.55)]",
            "hover:-translate-y-px hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.22),0_16px_30px_-12px_rgba(14,21,48,0.6)]",
            "disabled:opacity-60 disabled:cursor-not-allowed disabled:translate-y-0",
            "dark:bg-[var(--color-brand-500)]",
          )}
        >
          {syncing ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} />
          ) : (
            <RefreshCw
              className="h-3.5 w-3.5 transition-transform duration-300 group-hover:rotate-180"
              strokeWidth={2.5}
            />
          )}
          <span>{syncing ? "Pulling RIOT3" : "Refresh from RIOT3"}</span>
          <ArrowUpRight
            className="h-3.5 w-3.5 -mr-0.5 opacity-70 transition-transform duration-200 group-hover:translate-x-0.5"
            strokeWidth={2.5}
          />
        </button>
      </div>
    </motion.div>
  );
}

/* -------------------------------------------------------------------------- */
/* KpiStrip — four pastel KPI tiles with NumberTicker counters.               */
/* -------------------------------------------------------------------------- */
function KpiStrip({
  stats,
  lastSync,
  loading,
}: {
  stats: { total: number; active: number; linked: number; offline: number };
  lastSync: Date | null;
  loading: boolean;
}) {
  const ago = useTimeAgo(lastSync);
  const tiles = [
    {
      label: "Stations",
      hint: "All known nodes",
      value: stats.total,
      variant: "pastel-sky" as const,
      icon: <MapPin className="h-4 w-4" strokeWidth={2.2} />,
    },
    {
      label: "Active",
      hint: "Currently reachable",
      value: stats.active,
      variant: "pastel-mint" as const,
      icon: <Signal className="h-4 w-4" strokeWidth={2.2} />,
    },
    {
      label: "RIOT3-linked",
      hint: "Sourced from vendor",
      value: stats.linked,
      variant: "pastel-lavender" as const,
      icon: <Sparkles className="h-4 w-4" strokeWidth={2.2} />,
    },
    {
      label: "Force-offline",
      hint: "Manual overrides",
      value: stats.offline,
      variant: "pastel-peach" as const,
      icon: <AlertTriangle className="h-4 w-4" strokeWidth={2.2} />,
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-3 md:grid-cols-4 md:gap-4">
      {tiles.map((t, i) => (
        <motion.div
          key={t.label}
          initial={{ opacity: 0, y: 14 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{
            duration: 0.5,
            delay: 0.08 + i * 0.06,
            ease: [0.22, 1, 0.36, 1],
          }}
        >
          <GlassCard variant={t.variant} className="p-4 md:p-5 h-full">
            <div className="flex items-start justify-between gap-2">
              <div className="grid h-8 w-8 place-items-center rounded-[12px] bg-white/55 dark:bg-white/[0.08] text-[var(--color-ink-800)] shadow-[inset_0_1px_0_rgba(255,255,255,0.7)]">
                {t.icon}
              </div>
              <span className="font-mono text-[10px] tracking-[0.14em] uppercase opacity-60">
                {t.hint}
              </span>
            </div>
            <div className="mt-3 font-display text-[2.2rem] leading-none font-semibold tracking-tight">
              {loading ? (
                <span className="opacity-40">—</span>
              ) : (
                <NumberTicker value={t.value} />
              )}
            </div>
            <div className="mt-1 text-[12px] font-medium tracking-tight opacity-80">
              {t.label}
            </div>
          </GlassCard>
        </motion.div>
      ))}
      <motion.div
        initial={{ opacity: 0, y: 14 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.36, ease: [0.22, 1, 0.36, 1] }}
        className="col-span-2 md:col-span-4"
      >
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] font-mono tracking-tight text-[var(--color-ink-500)]">
          <span className="inline-flex items-center gap-1.5">
            <Activity className="h-3 w-3" strokeWidth={2.4} />
            last sync · {lastSync ? ago : "never"}
          </span>
          {lastSync && (
            <span className="inline-flex items-center gap-1.5 opacity-70">
              <span>{lastSync.toLocaleTimeString()}</span>
            </span>
          )}
        </div>
      </motion.div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* MapSelectorRail — horizontal pill rail of maps.                            */
/* Renders nothing when there's a single map (selector is implicit), and a    */
/* subtle skeleton row while loading.                                         */
/* -------------------------------------------------------------------------- */
function MapSelectorRail({
  maps,
  selectedId,
  onSelect,
  loading,
}: {
  maps: MapSummaryDto[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  loading: boolean;
}) {
  if (loading) {
    return (
      <div className="flex gap-2 overflow-hidden">
        {[0, 1, 2].map((i) => (
          <div
            key={i}
            className="h-12 w-44 rounded-[18px] bg-white/40 dark:bg-white/[0.04] animate-pulse"
          />
        ))}
      </div>
    );
  }

  if (maps.length === 0) return null;

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, delay: 0.12, ease: [0.22, 1, 0.36, 1] }}
      className="-mx-1 flex gap-2 overflow-x-auto pb-1 pl-1 pr-1"
      role="tablist"
      aria-label="Maps"
    >
      {maps.map((m, i) => {
        const active = m.id === selectedId;
        return (
          <motion.button
            key={m.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onSelect(m.id)}
            initial={{ opacity: 0, x: -6 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{
              duration: 0.4,
              delay: 0.14 + i * 0.06,
              ease: [0.22, 1, 0.36, 1],
            }}
            whileHover={{ y: -1 }}
            className={cn(
              "group relative flex shrink-0 items-center gap-3 rounded-[18px] px-4 py-2.5 text-left transition-all duration-200 cursor-pointer",
              "border",
              active
                ? "bg-white text-[var(--color-ink-900)] border-transparent shadow-[inset_0_1px_0_rgba(255,255,255,1),0_18px_36px_-14px_rgba(14,21,48,0.25)] dark:bg-[var(--color-surface-soft)]"
                : "bg-white/60 dark:bg-white/[0.04] border-white/70 dark:border-white/[0.06] text-[var(--color-ink-700)] hover:bg-white/85 dark:hover:bg-white/[0.08]",
            )}
          >
            {active && (
              <motion.span
                layoutId="map-selector-active"
                className="absolute -inset-px rounded-[18px] ring-2 ring-[var(--color-brand-200)] dark:ring-[var(--color-brand-500)]/30 pointer-events-none"
                transition={{ type: "spring", stiffness: 380, damping: 32 }}
              />
            )}
            <span
              className={cn(
                "grid h-9 w-9 place-items-center rounded-[12px] shrink-0",
                active
                  ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
                  : "bg-[var(--color-ink-100)] text-[var(--color-ink-700)] dark:bg-white/[0.06]",
              )}
            >
              <MapIcon className="h-4 w-4" strokeWidth={2.2} />
            </span>
            <span className="min-w-0">
              <span className="block font-display text-[13px] font-semibold tracking-tight truncate max-w-[180px]">
                {m.name}
              </span>
              <span className="font-mono text-[10px] tracking-tight text-[var(--color-ink-500)]">
                {m.activeStationCount}/{m.stationCount} · {m.vendorRef ? `RIOT ${m.vendorRef}` : "local"}
              </span>
            </span>
          </motion.button>
        );
      })}
    </motion.div>
  );
}

/* -------------------------------------------------------------------------- */
/* CanvasCard — the centerpiece. A SVG cartography of stations laid out by    */
/* their world coordinates, with grid, axes, pulse, and hover tooltips.       */
/* -------------------------------------------------------------------------- */
function CanvasCard({
  map,
  stations,
  loading,
}: {
  map: MapSummaryDto | null;
  stations: StationDto[];
  loading: boolean;
}) {
  const [hoverId, setHoverId] = useState<string | null>(null);
  const [pinned, setPinned] = useState<string | null>(null);
  const [cursor, setCursor] = useState<{ x: number; y: number } | null>(null);

  // Compute the world bounds. Fall back to map.width/height if no stations.
  // 8% padding so dots never sit on the frame edge.
  const bounds = useMemo(() => {
    if (stations.length === 0) {
      const W = map?.width ?? 1000;
      const H = map?.height ?? 700;
      return { minX: 0, minY: 0, maxX: W, maxY: H };
    }
    const xs = stations.map((s) => s.x);
    const ys = stations.map((s) => s.y);
    let minX = Math.min(...xs);
    let maxX = Math.max(...xs);
    let minY = Math.min(...ys);
    let maxY = Math.max(...ys);
    const padX = Math.max((maxX - minX) * 0.08, 200);
    const padY = Math.max((maxY - minY) * 0.08, 200);
    minX -= padX;
    maxX += padX;
    minY -= padY;
    maxY += padY;
    return { minX, minY, maxX, maxY };
  }, [stations, map]);

  const viewW = bounds.maxX - bounds.minX;
  const viewH = bounds.maxY - bounds.minY;
  // Step picker for the major grid — aim for ~6 lines.
  const stepBase = Math.max(viewW, viewH) / 6;
  const magnitude = Math.pow(10, Math.floor(Math.log10(stepBase)));
  const normalized = stepBase / magnitude;
  const step =
    magnitude *
    (normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10);

  const gridLines = useMemo(() => {
    const xs: number[] = [];
    const ys: number[] = [];
    const startX = Math.floor(bounds.minX / step) * step;
    const startY = Math.floor(bounds.minY / step) * step;
    for (let x = startX; x <= bounds.maxX + 1; x += step) xs.push(x);
    for (let y = startY; y <= bounds.maxY + 1; y += step) ys.push(y);
    return { xs, ys };
  }, [bounds, step]);

  const active = pinned ?? hoverId;
  const activeStation = stations.find((s) => s.id === active) ?? null;

  return (
    <GlassCard variant="strong" className="overflow-hidden">
      {/* Top bar — map identity, dimensions, live indicator */}
      <div className="flex items-center justify-between gap-3 px-5 py-3.5 border-b border-white/40 dark:border-white/[0.06]">
        <div className="min-w-0 flex items-center gap-3">
          <span className="grid h-8 w-8 place-items-center rounded-[10px] bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_6px_14px_-6px_rgba(14,21,48,0.55)] dark:bg-[var(--color-brand-500)]">
            <Compass className="h-4 w-4" strokeWidth={2.2} />
          </span>
          <div className="min-w-0">
            <div className="font-display text-[14.5px] font-semibold tracking-tight text-[var(--color-ink-900)] truncate">
              {map?.name ?? "—"}
            </div>
            <div className="font-mono text-[10.5px] tracking-tight text-[var(--color-ink-500)] flex flex-wrap items-center gap-x-3">
              {map ? (
                <>
                  <span>
                    extent · {Math.round(map.width).toLocaleString()} × {Math.round(map.height).toLocaleString()}
                  </span>
                  <span className="opacity-50">·</span>
                  <span>grid step {Math.round(step).toLocaleString()}</span>
                </>
              ) : (
                <span>awaiting map</span>
              )}
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {cursor && (
            <span className="hidden md:inline-flex items-center gap-1.5 font-mono text-[10.5px] tabular-nums tracking-tight text-[var(--color-ink-500)] rounded-full px-2.5 py-1 bg-white/60 dark:bg-white/[0.04]">
              <Crosshair className="h-3 w-3" strokeWidth={2.4} />
              {Math.round(cursor.x).toLocaleString()}, {Math.round(cursor.y).toLocaleString()}
            </span>
          )}
          <span className="inline-flex items-center gap-1.5 rounded-full bg-white/65 dark:bg-white/[0.05] px-2.5 py-1 text-[10.5px] font-semibold tracking-tight text-[var(--color-ink-700)]">
            <StatusPulse tone="success" />
            live
          </span>
        </div>
      </div>

      <div
        className="relative"
        style={{ aspectRatio: `${Math.max(viewW / Math.max(viewH, 1), 0.5)} / 1` }}
      >
        {/* Decorative aurora behind canvas */}
        <span
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={{
            background:
              "radial-gradient(60% 80% at 20% 20%, rgba(199,204,255,0.35), transparent 65%), radial-gradient(60% 80% at 90% 90%, rgba(253,226,209,0.3), transparent 65%)",
          }}
        />

        {!loading && stations.length === 0 && <CanvasEmptyState />}

        <svg
          role="img"
          aria-label="Station topology"
          viewBox={`${bounds.minX} ${bounds.minY} ${viewW} ${viewH}`}
          preserveAspectRatio="xMidYMid meet"
          className="absolute inset-0 h-full w-full"
          onMouseMove={(e) => {
            const svg = e.currentTarget;
            const rect = svg.getBoundingClientRect();
            const localX = ((e.clientX - rect.left) / rect.width) * viewW + bounds.minX;
            const localY = ((e.clientY - rect.top) / rect.height) * viewH + bounds.minY;
            setCursor({ x: localX, y: localY });
          }}
          onMouseLeave={() => {
            setCursor(null);
            setHoverId(null);
          }}
        >
          <defs>
            <radialGradient id="canvas-vignette" cx="50%" cy="50%" r="65%">
              <stop offset="60%" stopColor="rgba(255,255,255,0)" />
              <stop offset="100%" stopColor="rgba(14,21,48,0.08)" />
            </radialGradient>
            <pattern
              id="canvas-grid-minor"
              width={step / 5}
              height={step / 5}
              patternUnits="userSpaceOnUse"
              x={Math.floor(bounds.minX / (step / 5)) * (step / 5)}
              y={Math.floor(bounds.minY / (step / 5)) * (step / 5)}
            >
              <path
                d={`M ${step / 5} 0 L 0 0 0 ${step / 5}`}
                fill="none"
                stroke="currentColor"
                strokeOpacity="0.05"
                strokeWidth={Math.max(viewW, viewH) / 2000}
              />
            </pattern>
            {Object.entries(TYPE_META).map(([key, m]) => (
              <radialGradient
                key={key}
                id={`station-glow-${key}`}
                cx="50%"
                cy="50%"
                r="50%"
              >
                <stop offset="0%" stopColor={m.swatch} stopOpacity="0.8" />
                <stop offset="60%" stopColor={m.swatch} stopOpacity="0.15" />
                <stop offset="100%" stopColor={m.swatch} stopOpacity="0" />
              </radialGradient>
            ))}
          </defs>

          {/* Minor grid wash */}
          <rect
            x={bounds.minX}
            y={bounds.minY}
            width={viewW}
            height={viewH}
            fill="url(#canvas-grid-minor)"
            className="text-[var(--color-ink-900)]"
          />

          {/* Major grid lines */}
          {gridLines.xs.map((x) => (
            <line
              key={`gx-${x}`}
              x1={x}
              y1={bounds.minY}
              x2={x}
              y2={bounds.maxY}
              stroke="currentColor"
              strokeOpacity={x === 0 ? 0.22 : 0.08}
              strokeWidth={Math.max(viewW, viewH) / 1500}
              className="text-[var(--color-ink-700)]"
            />
          ))}
          {gridLines.ys.map((y) => (
            <line
              key={`gy-${y}`}
              x1={bounds.minX}
              y1={y}
              x2={bounds.maxX}
              y2={y}
              stroke="currentColor"
              strokeOpacity={y === 0 ? 0.22 : 0.08}
              strokeWidth={Math.max(viewW, viewH) / 1500}
              className="text-[var(--color-ink-700)]"
            />
          ))}

          {/* Map boundary box (if known) */}
          {map && (
            <rect
              x={0}
              y={0}
              width={map.width}
              height={map.height}
              fill="none"
              stroke="currentColor"
              strokeOpacity={0.16}
              strokeDasharray={`${step / 10} ${step / 14}`}
              strokeWidth={Math.max(viewW, viewH) / 1100}
              className="text-[var(--color-brand-500)]"
            />
          )}

          {/* Stations — pulse for active, dim for inactive, ring for offline override */}
          {stations.map((s, i) => {
            const k = typeKey(s.type);
            const meta = TYPE_META[k];
            const isActiveDot = s.id === active;
            const r = Math.max(viewW, viewH) / 110;
            const ringR = r * 2.6;
            return (
              <motion.g
                key={s.id}
                initial={{ opacity: 0, scale: 0.4 }}
                animate={{ opacity: 1, scale: 1 }}
                transition={{
                  duration: 0.5,
                  delay: 0.2 + Math.min(i, 60) * 0.018,
                  ease: [0.22, 1, 0.36, 1],
                }}
                style={{ transformOrigin: `${s.x}px ${s.y}px` }}
                onMouseEnter={() => setHoverId(s.id)}
                onMouseLeave={() => setHoverId((id) => (id === s.id ? null : id))}
                onClick={() =>
                  setPinned((id) => (id === s.id ? null : s.id))
                }
                className="cursor-pointer"
              >
                {/* Soft halo */}
                {s.isActive && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={ringR}
                    fill={`url(#station-glow-${k})`}
                  />
                )}

                {/* Breathing ring on active */}
                {s.isActive && (
                  <motion.circle
                    cx={s.x}
                    cy={s.y}
                    r={r * 1.8}
                    fill="none"
                    stroke={meta.swatch}
                    strokeOpacity={0.35}
                    strokeWidth={Math.max(viewW, viewH) / 1500}
                    initial={{ opacity: 0.55, scale: 0.85 }}
                    animate={{
                      opacity: [0.55, 0, 0.55],
                      scale: [0.85, 1.6, 0.85],
                    }}
                    transition={{
                      duration: 2.4,
                      repeat: Infinity,
                      ease: "easeInOut",
                      delay: (i % 7) * 0.18,
                    }}
                    style={{ transformOrigin: `${s.x}px ${s.y}px` }}
                  />
                )}

                {/* Selected ring */}
                {isActiveDot && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={r * 2.2}
                    fill="none"
                    stroke={meta.swatch}
                    strokeWidth={Math.max(viewW, viewH) / 900}
                    strokeOpacity={0.9}
                  />
                )}

                {/* Manual-offline override */}
                {s.isManualOverrideActive && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={r * 2}
                    fill="none"
                    stroke="var(--color-amber)"
                    strokeWidth={Math.max(viewW, viewH) / 1200}
                    strokeDasharray={`${r * 0.6} ${r * 0.4}`}
                  />
                )}

                {/* Core dot */}
                <circle
                  cx={s.x}
                  cy={s.y}
                  r={r}
                  fill={meta.swatch}
                  fillOpacity={s.isActive ? 1 : 0.35}
                  stroke="white"
                  strokeOpacity={0.85}
                  strokeWidth={Math.max(viewW, viewH) / 1400}
                />

                {/* Inactive cross */}
                {!s.isActive && (
                  <g
                    stroke="white"
                    strokeOpacity={0.85}
                    strokeWidth={Math.max(viewW, viewH) / 1400}
                    strokeLinecap="round"
                  >
                    <line
                      x1={s.x - r * 0.5}
                      y1={s.y - r * 0.5}
                      x2={s.x + r * 0.5}
                      y2={s.y + r * 0.5}
                    />
                    <line
                      x1={s.x - r * 0.5}
                      y1={s.y + r * 0.5}
                      x2={s.x + r * 0.5}
                      y2={s.y - r * 0.5}
                    />
                  </g>
                )}
              </motion.g>
            );
          })}

          {/* Origin marker */}
          <g>
            <circle
              cx={0}
              cy={0}
              r={Math.max(viewW, viewH) / 250}
              fill="var(--color-ink-700)"
              fillOpacity={0.45}
            />
          </g>

          {/* Axis labels — major gridline coordinates in mono */}
          {gridLines.xs.map((x) => (
            <text
              key={`tx-${x}`}
              x={x}
              y={bounds.minY + Math.max(viewW, viewH) / 50}
              fontSize={Math.max(viewW, viewH) / 60}
              fontFamily="var(--font-mono)"
              fill="currentColor"
              fillOpacity={0.4}
              textAnchor="middle"
              className="text-[var(--color-ink-700)]"
            >
              {Math.round(x).toLocaleString()}
            </text>
          ))}
          {gridLines.ys.map((y) => (
            <text
              key={`ty-${y}`}
              x={bounds.minX + Math.max(viewW, viewH) / 80}
              y={y}
              fontSize={Math.max(viewW, viewH) / 60}
              fontFamily="var(--font-mono)"
              fill="currentColor"
              fillOpacity={0.4}
              dominantBaseline="middle"
              className="text-[var(--color-ink-700)]"
            >
              {Math.round(y).toLocaleString()}
            </text>
          ))}

          {/* Vignette */}
          <rect
            x={bounds.minX}
            y={bounds.minY}
            width={viewW}
            height={viewH}
            fill="url(#canvas-vignette)"
            pointerEvents="none"
          />
        </svg>

        {/* Floating compass + hint */}
        <div className="pointer-events-none absolute top-4 right-4 flex items-center gap-2">
          <motion.div
            animate={{ rotate: [0, 6, -4, 0] }}
            transition={{ duration: 14, repeat: Infinity, ease: "easeInOut" }}
            className="glass-bubble grid h-10 w-10 place-items-center rounded-full text-[var(--color-ink-700)]"
          >
            <Navigation className="h-4 w-4" strokeWidth={2.2} />
          </motion.div>
        </div>

        {/* Pinned station detail */}
        <AnimatePresence>
          {activeStation && (
            <motion.div
              key={activeStation.id}
              initial={{ opacity: 0, y: 8, scale: 0.97 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: 8, scale: 0.97 }}
              transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
              className="pointer-events-auto absolute left-4 bottom-4 max-w-[320px] glass-strong rounded-[20px] p-4"
            >
              <button
                type="button"
                aria-label="Dismiss"
                onClick={() => {
                  setPinned(null);
                  setHoverId(null);
                }}
                className="absolute right-2 top-2 grid h-7 w-7 place-items-center rounded-full text-[var(--color-ink-500)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer"
              >
                <X className="h-3.5 w-3.5" strokeWidth={2.2} />
              </button>
              <StationDetail station={activeStation} />
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </GlassCard>
  );
}

function CanvasEmptyState() {
  return (
    <div className="absolute inset-0 grid place-items-center text-center px-6">
      <div className="max-w-sm">
        <div className="mx-auto grid h-12 w-12 place-items-center rounded-full bg-white/70 dark:bg-white/[0.06] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_8px_18px_-8px_rgba(15,23,42,0.25)] text-[var(--color-ink-700)]">
          <Maximize2 className="h-5 w-5" strokeWidth={2.2} />
        </div>
        <h3 className="mt-3 font-display text-[1.05rem] font-semibold tracking-tight text-[var(--color-ink-800)]">
          No stations on this map yet
        </h3>
        <p className="mt-1 text-[12.5px] text-[var(--color-ink-500)]">
          Refresh from RIOT3 to pull the latest topology, or add stations manually from the Facility module.
        </p>
      </div>
    </div>
  );
}

function StationDetail({ station }: { station: StationDto }) {
  const k = typeKey(station.type);
  const meta = TYPE_META[k];
  const Icon = meta.icon;
  return (
    <div>
      <div className="flex items-start gap-3">
        <span
          className="grid h-9 w-9 place-items-center rounded-[12px] text-white shrink-0"
          style={{ background: meta.swatch }}
        >
          <Icon className="h-4 w-4" strokeWidth={2.2} />
        </span>
        <div className="min-w-0">
          <div className="font-display text-[14px] font-semibold tracking-tight text-[var(--color-ink-900)] truncate">
            {station.name}
          </div>
          <div className="font-mono text-[10.5px] tracking-tight text-[var(--color-ink-500)] truncate">
            {station.code ?? "—"} · {meta.label}
          </div>
        </div>
      </div>
      <div className="mt-3 grid grid-cols-2 gap-2 text-[11px]">
        <Detail label="x" value={Math.round(station.x).toLocaleString()} mono />
        <Detail label="y" value={Math.round(station.y).toLocaleString()} mono />
        <Detail
          label="θ"
          value={station.theta !== null ? `${station.theta.toFixed(2)} rad` : "—"}
          mono
        />
        <Detail label="RIOT" value={station.vendorRef ?? "—"} mono />
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        <Pill tone={station.isActive ? "ok" : "muted"}>
          {station.isActive ? "active" : "inactive"}
        </Pill>
        {station.isManualOverrideActive && (
          <Pill tone="warn">force-offline</Pill>
        )}
        {!station.vendorRef && <Pill tone="muted">manual entry</Pill>}
      </div>
    </div>
  );
}

function Detail({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="rounded-[10px] bg-white/55 dark:bg-white/[0.04] px-2.5 py-1.5">
      <div className="text-[9.5px] uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
        {label}
      </div>
      <div
        className={cn(
          "mt-0.5 text-[12px] font-semibold text-[var(--color-ink-800)] truncate",
          mono && "font-mono tabular-nums tracking-tight",
        )}
      >
        {value}
      </div>
    </div>
  );
}

function Pill({
  children,
  tone = "ok",
}: {
  children: React.ReactNode;
  tone?: "ok" | "warn" | "muted";
}) {
  const cls = {
    ok: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
    warn: "bg-[var(--color-amber-soft)] text-[#8a4a07] dark:text-[var(--color-amber)]",
    muted: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
  }[tone];
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold tracking-tight",
        cls,
      )}
    >
      {children}
    </span>
  );
}

/* -------------------------------------------------------------------------- */
/* SyncCommandCard — primary action surface for pulling from RIOT3.           */
/* -------------------------------------------------------------------------- */
function SyncCommandCard({
  map,
  syncing,
  onSync,
  lastSync,
  lastResult,
}: {
  map: MapSummaryDto | null;
  syncing: boolean;
  onSync: () => void;
  lastSync: Date | null;
  lastResult: SyncMapStationsResultDto | null;
}) {
  const canSync = !!map?.vendorRef;
  const ago = useTimeAgo(lastSync);

  return (
    <GlassCard variant="ink" className="p-5 md:p-6 relative overflow-hidden">
      <span
        aria-hidden
        className="pointer-events-none absolute -top-20 -right-12 h-56 w-56 rounded-full opacity-60 blur-[80px]"
        style={{
          background:
            "radial-gradient(circle, rgba(143,156,255,0.55), transparent 60%)",
        }}
      />
      <div className="relative flex items-center justify-between">
        <div className="text-[10px] font-bold uppercase tracking-[0.18em] text-white/55">
          Command
        </div>
        <span className="inline-flex items-center gap-1 rounded-full bg-white/10 px-2 py-0.5 text-[9.5px] font-mono tracking-tight text-white/70">
          riot3 · {map?.vendorRef ?? "—"}
        </span>
      </div>
      <h3 className="relative mt-2 font-display text-[1.45rem] font-semibold tracking-tight text-white leading-tight">
        Pull live topology
      </h3>
      <p className="relative mt-1.5 text-[12.5px] text-white/65 max-w-[28ch]">
        {canSync
          ? "Match every station against RIOT3 and reconcile add, update, reactivate, and deactivate in one transaction."
          : "This map isn't linked to a RIOT3 vendor — import it from the Facility module to enable refresh."}
      </p>

      <button
        type="button"
        onClick={onSync}
        disabled={!canSync || syncing}
        className={cn(
          "relative mt-5 group inline-flex w-full items-center justify-between gap-3 rounded-full px-4 h-12 text-[13px] font-semibold tracking-tight transition-all duration-200 cursor-pointer",
          "bg-white text-[var(--color-brand-900)]",
          "shadow-[inset_0_1px_0_rgba(255,255,255,0.95),0_18px_30px_-12px_rgba(0,0,0,0.45)]",
          "hover:-translate-y-px",
          "disabled:opacity-50 disabled:cursor-not-allowed disabled:translate-y-0",
        )}
      >
        <span className="flex items-center gap-2.5">
          <span className="grid h-8 w-8 place-items-center rounded-full bg-[var(--color-brand-900)] text-white">
            {syncing ? (
              <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.4} />
            ) : (
              <Zap className="h-4 w-4" strokeWidth={2.4} />
            )}
          </span>
          <span>{syncing ? "Pulling RIOT3" : "Refresh stations"}</span>
        </span>
        <ArrowUpRight
          className="h-4 w-4 transition-transform duration-200 group-hover:translate-x-0.5 group-hover:-translate-y-0.5"
          strokeWidth={2.4}
        />
      </button>

      <div className="relative mt-4 grid grid-cols-2 gap-1.5">
        <SyncStat label="added" value={lastResult?.added ?? null} tone="add" />
        <SyncStat label="updated" value={lastResult?.updated ?? null} tone="upd" />
        <SyncStat
          label="reactivated"
          value={lastResult?.reactivated ?? null}
          tone="re"
        />
        <SyncStat
          label="deactivated"
          value={lastResult?.deactivated ?? null}
          tone="off"
        />
      </div>
      <div className="relative mt-4 flex items-center justify-between text-[11px] text-white/60 font-mono tracking-tight">
        <span className="inline-flex items-center gap-1.5">
          <span
            className={cn(
              "h-1.5 w-1.5 rounded-full",
              lastSync ? "bg-[var(--color-success)]" : "bg-white/20",
            )}
          />
          {lastSync ? `synced ${ago}` : "never synced"}
        </span>
        <span>{lastSync ? lastSync.toLocaleTimeString() : ""}</span>
      </div>
    </GlassCard>
  );
}

function SyncStat({
  label,
  value,
  tone,
}: {
  label: string;
  value: number | null;
  tone: "add" | "upd" | "re" | "off";
}) {
  const toneColor = {
    add: "text-[var(--color-success)]",
    upd: "text-[#9aa6ff]",
    re: "text-[var(--color-amber)]",
    off: "text-[var(--color-coral)]",
  }[tone];
  return (
    <div className="rounded-[12px] bg-white/[0.06] border border-white/[0.06] px-3 py-2">
      <div className="text-[9.5px] uppercase tracking-[0.16em] text-white/40">
        {label}
      </div>
      <div className={cn("mt-0.5 font-display text-[18px] font-semibold tabular-nums", toneColor)}>
        {value === null ? "—" : value}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* FacilityInfoCard — map metadata + station ratio bar.                       */
/* -------------------------------------------------------------------------- */
function FacilityInfoCard({
  map,
  stats,
}: {
  map: MapSummaryDto | null;
  stats: { total: number; active: number; linked: number; offline: number };
}) {
  const activePct = stats.total > 0 ? (stats.active / stats.total) * 100 : 0;
  return (
    <GlassCard variant="strong" className="p-5 md:p-6">
      <div className="flex items-center justify-between">
        <div className="text-[10px] font-bold uppercase tracking-[0.18em] text-[var(--color-ink-400)]">
          Map metadata
        </div>
        <span className="font-mono text-[10px] tracking-tight text-[var(--color-ink-500)]">
          {map?.version ?? "—"}
        </span>
      </div>
      <div className="mt-3 grid grid-cols-2 gap-2">
        <KV label="extent w" value={map ? Math.round(map.width).toLocaleString() : "—"} />
        <KV label="extent h" value={map ? Math.round(map.height).toLocaleString() : "—"} />
        <KV label="vendor" value={map?.vendorRef ?? "manual"} />
        <KV label="stations" value={String(stats.total)} />
      </div>

      <div className="mt-5">
        <div className="flex items-end justify-between text-[11.5px]">
          <span className="font-medium text-[var(--color-ink-700)]">
            Active ratio
          </span>
          <span className="font-mono tabular-nums text-[var(--color-ink-500)]">
            {Math.round(activePct)}%
          </span>
        </div>
        <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-[var(--color-ink-100)] dark:bg-white/[0.06]">
          <motion.div
            initial={{ width: 0 }}
            animate={{ width: `${activePct}%` }}
            transition={{ duration: 0.8, ease: [0.22, 1, 0.36, 1] }}
            className="h-full rounded-full bg-gradient-to-r from-[var(--color-success)] to-[#7ee2c1]"
          />
        </div>
      </div>
    </GlassCard>
  );
}

function KV({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-[10px] bg-white/55 dark:bg-white/[0.04] px-2.5 py-1.5">
      <div className="text-[9.5px] uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
        {label}
      </div>
      <div className="mt-0.5 font-mono text-[12px] tabular-nums tracking-tight text-[var(--color-ink-800)] truncate">
        {value}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* LegendCard — station-type taxonomy with live counts.                       */
/* -------------------------------------------------------------------------- */
function LegendCard({ stations }: { stations: StationDto[] }) {
  const counts = useMemo(() => {
    const map: Partial<Record<TypeKey, number>> = {};
    for (const s of stations) {
      const k = typeKey(s.type);
      map[k] = (map[k] ?? 0) + 1;
    }
    return map;
  }, [stations]);
  return (
    <GlassCard variant="default" className="p-5 md:p-6">
      <div className="flex items-center justify-between">
        <div className="text-[10px] font-bold uppercase tracking-[0.18em] text-[var(--color-ink-400)]">
          Taxonomy
        </div>
        <span className="font-mono text-[10px] tracking-tight text-[var(--color-ink-500)]">
          {Object.keys(counts).length} kinds
        </span>
      </div>
      <ul className="mt-3 space-y-1">
        {TYPE_KEYS.map((k) => {
          const meta = TYPE_META[k];
          const count = counts[k] ?? 0;
          return (
            <li
              key={k}
              className="flex items-center justify-between rounded-[10px] px-2.5 py-1.5 hover:bg-white/55 dark:hover:bg-white/[0.04] transition-colors"
            >
              <span className="flex items-center gap-2.5">
                <span
                  className="h-2.5 w-2.5 rounded-full"
                  style={{ background: meta.swatch }}
                />
                <span className="text-[12px] font-medium tracking-tight text-[var(--color-ink-800)]">
                  {meta.label}
                </span>
              </span>
              <span
                className={cn(
                  "font-mono text-[11px] tabular-nums",
                  count > 0 ? "text-[var(--color-ink-700)]" : "text-[var(--color-ink-300)]",
                )}
              >
                {count}
              </span>
            </li>
          );
        })}
      </ul>
    </GlassCard>
  );
}

/* -------------------------------------------------------------------------- */
/* StationDirectory — searchable, filter-pillable table of all stations.       */
/* -------------------------------------------------------------------------- */
function StationDirectory({
  stations,
  loading,
  error,
}: {
  stations: StationDto[];
  loading: boolean;
  error: string | null;
}) {
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState<TypeKey | "ALL">("ALL");
  const [activeOnly, setActiveOnly] = useState(false);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return stations.filter((s) => {
      if (activeOnly && !s.isActive) return false;
      if (typeFilter !== "ALL" && typeKey(s.type) !== typeFilter) return false;
      if (!term) return true;
      return (
        s.name.toLowerCase().includes(term) ||
        (s.code?.toLowerCase().includes(term) ?? false) ||
        (s.vendorRef?.toLowerCase().includes(term) ?? false)
      );
    });
  }, [stations, search, typeFilter, activeOnly]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 14 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.4, ease: [0.22, 1, 0.36, 1] }}
    >
      <GlassCard variant="strong" className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 px-5 md:px-6 py-4 border-b border-white/40 dark:border-white/[0.06]">
          <SectionLabel
            icon={<Layers className="h-4 w-4" strokeWidth={2} />}
            title="Station directory"
            subtitle={`${filtered.length} of ${stations.length} stations`}
          />

          <div className="ml-auto flex flex-wrap items-center gap-2">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-[var(--color-ink-400)]" strokeWidth={2.2} />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search name, code, RIOT id…"
                aria-label="Search stations"
                className="h-9 w-64 max-w-[60vw] rounded-full bg-white/65 dark:bg-white/[0.05] border border-white/70 dark:border-white/[0.06] pl-8 pr-3 text-[12.5px] text-[var(--color-ink-800)] placeholder:text-[var(--color-ink-400)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40"
              />
            </div>

            <button
              type="button"
              onClick={() => setActiveOnly((v) => !v)}
              className={cn(
                "inline-flex items-center gap-1.5 rounded-full px-3 h-9 text-[11.5px] font-medium tracking-tight transition-colors cursor-pointer",
                activeOnly
                  ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
                  : "bg-white/65 dark:bg-white/[0.05] text-[var(--color-ink-700)] hover:bg-white/85",
              )}
            >
              <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.2} />
              Active only
            </button>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-1.5 px-5 md:px-6 py-3 border-b border-white/40 dark:border-white/[0.06]">
          <TypeChip
            active={typeFilter === "ALL"}
            onClick={() => setTypeFilter("ALL")}
            color="var(--color-ink-700)"
          >
            All
          </TypeChip>
          {TYPE_KEYS.map((k) => (
            <TypeChip
              key={k}
              active={typeFilter === k}
              onClick={() => setTypeFilter(typeFilter === k ? "ALL" : k)}
              color={TYPE_META[k].swatch}
            >
              {TYPE_META[k].label}
            </TypeChip>
          ))}
        </div>

        <div className="overflow-x-auto">
          {error ? (
            <div className="p-8 text-center text-[13px] text-[var(--color-coral)]">
              {error}
            </div>
          ) : loading && stations.length === 0 ? (
            <div className="p-8 text-center text-[13px] text-[var(--color-ink-500)]">
              Loading topology…
            </div>
          ) : filtered.length === 0 ? (
            <div className="p-10 text-center">
              <div className="font-display text-[14px] font-semibold text-[var(--color-ink-800)]">
                No stations match
              </div>
              <p className="mt-1 text-[12.5px] text-[var(--color-ink-500)]">
                Clear filters or refresh from RIOT3 to seed new stations.
              </p>
            </div>
          ) : (
            <table className="min-w-full text-[12.5px]">
              <thead>
                <tr className="text-left text-[10px] uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
                  <th className="px-5 md:px-6 py-2.5 font-semibold">Code</th>
                  <th className="py-2.5 font-semibold">Name</th>
                  <th className="py-2.5 font-semibold">Type</th>
                  <th className="py-2.5 font-semibold">Coords</th>
                  <th className="py-2.5 font-semibold">RIOT</th>
                  <th className="py-2.5 font-semibold">Status</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((s, i) => {
                  const k = typeKey(s.type);
                  const meta = TYPE_META[k];
                  return (
                    <motion.tr
                      key={s.id}
                      initial={{ opacity: 0, y: 4 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{
                        duration: 0.28,
                        delay: Math.min(i, 30) * 0.015,
                      }}
                      className="border-t border-white/30 dark:border-white/[0.04] hover:bg-white/45 dark:hover:bg-white/[0.04] transition-colors"
                    >
                      <td className="px-5 md:px-6 py-3 font-mono text-[11.5px] font-semibold tabular-nums tracking-tight text-[var(--color-ink-900)]">
                        {s.code ?? "—"}
                      </td>
                      <td className="py-3 font-medium tracking-tight text-[var(--color-ink-800)]">
                        {s.name}
                      </td>
                      <td className="py-3">
                        <span
                          className={cn(
                            "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[10.5px] font-semibold tracking-tight",
                            meta.chip,
                          )}
                        >
                          <span
                            className="h-1.5 w-1.5 rounded-full"
                            style={{ background: meta.swatch }}
                          />
                          {meta.label}
                        </span>
                      </td>
                      <td className="py-3 font-mono tabular-nums text-[11px] tracking-tight text-[var(--color-ink-600)] whitespace-nowrap">
                        {Math.round(s.x).toLocaleString()},{" "}
                        {Math.round(s.y).toLocaleString()}
                      </td>
                      <td className="py-3 font-mono text-[11px] tabular-nums tracking-tight text-[var(--color-ink-600)]">
                        {s.vendorRef ?? <span className="text-[var(--color-ink-300)]">—</span>}
                      </td>
                      <td className="py-3 pr-5">
                        <span className="inline-flex items-center gap-2">
                          {s.isActive ? (
                            <Pill tone="ok">active</Pill>
                          ) : (
                            <Pill tone="muted">inactive</Pill>
                          )}
                          {s.isManualOverrideActive && (
                            <Pill tone="warn">offline</Pill>
                          )}
                        </span>
                      </td>
                    </motion.tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </GlassCard>
    </motion.div>
  );
}

function TypeChip({
  children,
  active,
  onClick,
  color,
}: {
  children: React.ReactNode;
  active: boolean;
  onClick: () => void;
  color: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 h-7 text-[11px] font-medium tracking-tight transition-colors cursor-pointer",
        active
          ? "bg-[var(--color-ink-900)] text-white dark:bg-white/[0.12]"
          : "bg-white/55 dark:bg-white/[0.04] text-[var(--color-ink-700)] hover:bg-white/80",
      )}
    >
      <span
        className="h-1.5 w-1.5 rounded-full"
        style={{ background: color }}
      />
      {children}
    </button>
  );
}

/* -------------------------------------------------------------------------- */
/* Toast — bottom-center notification with success / error tone.              */
/* -------------------------------------------------------------------------- */
function Toast({
  toast,
  onDismiss,
}: {
  toast: { kind: "ok" | "err"; msg: string } | null;
  onDismiss: () => void;
}) {
  return (
    <AnimatePresence>
      {toast && (
        <motion.div
          key={toast.msg}
          initial={{ opacity: 0, y: 16, scale: 0.97 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: 16, scale: 0.97 }}
          transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
          className="fixed bottom-6 left-1/2 z-50 -translate-x-1/2"
        >
          <div
            className={cn(
              "flex items-center gap-3 rounded-full pl-4 pr-2 py-2 text-[12.5px] font-medium tracking-tight",
              "glass-strong shadow-[0_30px_60px_-20px_rgba(15,23,42,0.4)]",
            )}
          >
            <span
              className={cn(
                "grid h-7 w-7 place-items-center rounded-full",
                toast.kind === "ok"
                  ? "bg-[var(--color-success)] text-white"
                  : "bg-[var(--color-coral)] text-white",
              )}
            >
              {toast.kind === "ok" ? (
                <CheckCircle2 className="h-4 w-4" strokeWidth={2.2} />
              ) : (
                <AlertTriangle className="h-4 w-4" strokeWidth={2.2} />
              )}
            </span>
            <span className="text-[var(--color-ink-800)]">{toast.msg}</span>
            <button
              type="button"
              aria-label="Dismiss"
              onClick={onDismiss}
              className="grid h-7 w-7 place-items-center rounded-full text-[var(--color-ink-500)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer"
            >
              <X className="h-3.5 w-3.5" strokeWidth={2.2} />
            </button>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
