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
  Hand,
  HelpCircle,
  Hexagon,
  Layers,
  Loader2,
  Map as MapIcon,
  MapPin,
  Maximize,
  Maximize2,
  Minimize,
  Minus,
  Mouse,
  Navigation,
  Plus,
  PackageOpen,
  ParkingCircle,
  Pencil,
  RefreshCw,
  RotateCw,
  Search,
  Signal,
  Sparkles,
  X,
} from "lucide-react";

import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { SectionLabel } from "@/components/primitives/section-label";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { DateTime } from "@/components/primitives/date-time";
import { cn } from "@/lib/utils";
import {
  clearStationOverride,
  forceStationOffline,
  getStations,
  listMaps,
  syncMapStations,
  updateStation,
  type MapSummaryDto,
  type StationDto,
} from "@/lib/api/facility";
import {
  StationEditDrawer,
  type StationOp,
} from "@/components/facility/station-edit-drawer";
import {
  RobotLayer,
  RobotTooltip,
  useRobotPositions,
} from "@/components/facility/robot-layer";

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

/* -------------------------------------------------------------------------- */
/* Rotation math — the SVG is CSS-rotated, but gesture handlers think in the  */
/* un-rotated frame (because viewBox + world coords don't know about it). We  */
/* convert pointer events / deltas / world-derived overlay positions in/out   */
/* of the rotated frame via plain 2D rotation matrices around the SVG center. */
/* -------------------------------------------------------------------------- */

// screen cursor → un-rotated SVG pixel position
function screenToSvg(clientX: number, clientY: number, rect: DOMRect, rotation: number) {
  const x = clientX - rect.left;
  const y = clientY - rect.top;
  if (rotation === 0) return { x, y };
  const cx = rect.width / 2;
  const cy = rect.height / 2;
  const dx = x - cx;
  const dy = y - cy;
  const rad = (-rotation * Math.PI) / 180;
  const cos = Math.cos(rad);
  const sin = Math.sin(rad);
  return { x: dx * cos - dy * sin + cx, y: dx * sin + dy * cos + cy };
}

// screen-space delta → un-rotated delta (so drag direction follows the finger)
function rotateDelta(dxPx: number, dyPx: number, rotation: number) {
  if (rotation === 0) return { dx: dxPx, dy: dyPx };
  const rad = (-rotation * Math.PI) / 180;
  const cos = Math.cos(rad);
  const sin = Math.sin(rad);
  return { dx: dxPx * cos - dyPx * sin, dy: dxPx * sin + dyPx * cos };
}

// un-rotated SVG pixel position → screen position (forward rotation, for
// HTML overlays that mirror a world-space anchor, e.g. the robot tooltip)
function svgToScreen(svgX: number, svgY: number, rect: DOMRect, rotation: number) {
  if (rotation === 0) return { x: svgX, y: svgY };
  const cx = rect.width / 2;
  const cy = rect.height / 2;
  const dx = svgX - cx;
  const dy = svgY - cy;
  const rad = (rotation * Math.PI) / 180;
  const cos = Math.cos(rad);
  const sin = Math.sin(rad);
  return { x: dx * cos - dy * sin + cx, y: dx * sin + dy * cos + cy };
}

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
  const [cooldownRemaining, setCooldownRemaining] = useState(0);
  const [lastSync, setLastSync] = useState<Date | null>(null);
  const [toast, setToast] = useState<{
    kind: "ok" | "err";
    msg: string;
  } | null>(null);
  // In-place edit drawer state. Driven by canvas clicks + table double-clicks.
  const [editingStationId, setEditingStationId] = useState<string | null>(null);
  const [editBusy, setEditBusy] = useState(false);

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

  // Sync cooldown countdown — ticks once per second until it hits zero.
  // Set by onSync after every attempt (success or failure) to throttle
  // rage-clicks against the external RIOT3 vendor.
  useEffect(() => {
    if (cooldownRemaining <= 0) return;
    const t = window.setInterval(() => {
      setCooldownRemaining((n) => (n <= 1 ? 0 : n - 1));
    }, 1000);
    return () => window.clearInterval(t);
  }, [cooldownRemaining]);

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

  // Drawer submit — routes intent to the right backend endpoint, then
  // refreshes the station list so the canvas + table reflect the change.
  // Toasts surface success / failure; the drawer stays open on error so the
  // operator can correct the input.
  const handleEditSubmit = useCallback(
    async (station: StationDto, op: StationOp) => {
      if (!selectedMapId) return;
      setEditBusy(true);
      try {
        if (op.kind === "update") {
          await updateStation(station.id, { type: op.type, code: op.code });
          setToast({ kind: "ok", msg: `Saved ${station.name}` });
        } else if (op.kind === "force-offline") {
          await forceStationOffline(station.id, {
            reason: op.reason,
            durationMinutes: op.durationMinutes,
          });
          setToast({ kind: "ok", msg: `${station.name} force-offline for ${op.durationMinutes}m` });
        } else {
          await clearStationOverride(station.id);
          setToast({ kind: "ok", msg: `${station.name} restored` });
        }
        await refreshStations(selectedMapId);
      } catch (err) {
        setToast({
          kind: "err",
          msg: err instanceof Error ? err.message : "Save failed",
        });
      } finally {
        setEditBusy(false);
      }
    },
    [selectedMapId, refreshStations],
  );

  const editingStation = useMemo(
    () => stations.find((s) => s.id === editingStationId) ?? null,
    [stations, editingStationId],
  );

  const onSync = useCallback(async () => {
    if (!selectedMapId || syncing || cooldownRemaining > 0) return;
    setSyncing(true);
    try {
      const result = await syncMapStations(selectedMapId);
      setLastSync(new Date());
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
      setCooldownRemaining(4);
    }
  }, [selectedMapId, syncing, cooldownRemaining, refreshStations]);

  return (
    <div className="space-y-6 md:space-y-7">
      <HeaderStrip
        mapName={selectedMap?.name ?? null}
        onSync={onSync}
        syncing={syncing}
        canSync={!!selectedMap?.vendorRef}
        cooldownRemaining={cooldownRemaining}
      />

      <KpiStrip stats={stats} lastSync={lastSync} loading={loading} />

      {/* /facility/maps is now the station-management surface — the live
          cartography lives on /home (see HeroLiveMap → CanvasCard). The
          map selector still scopes which map's stations the directory and
          KPI strip show, so it stays. */}
      <MapSelectorRail
        maps={maps}
        selectedId={selectedMapId}
        onSelect={setSelectedMapId}
        loading={loading && maps.length === 0}
      />

      <StationDirectory
        stations={stations}
        loading={loading}
        error={error}
        onEditStation={setEditingStationId}
      />

      <StationEditDrawer
        open={editingStationId !== null}
        station={editingStation}
        busy={editBusy}
        onClose={() => setEditingStationId(null)}
        onSubmit={handleEditSubmit}
      />

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
  cooldownRemaining,
}: {
  mapName: string | null;
  onSync: () => void;
  syncing: boolean;
  canSync: boolean;
  cooldownRemaining: number;
}) {
  const cooling = cooldownRemaining > 0;
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
          disabled={syncing || !canSync || cooling}
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
          <span>
            {syncing
              ? "Pulling RIOT3"
              : cooling
                ? `Wait ${cooldownRemaining}s…`
                : "Refresh from RIOT3"}
          </span>
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
              <DateTime value={lastSync} variant="time" />
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
/* Exported so other surfaces (e.g. /home) can render the same map without    */
/* duplicating the SVG / gesture / robot-layer pipeline.                      */
/*  - `className`       — extra classes for the GlassCard wrapper             */
/*  - `canvasClassName` — overrides the default canvas-height clamp           */
/*  - `onEditStation`   — when omitted, the "Edit station" CTA is hidden so   */
/*                        the card is fully read-only                         */
/* -------------------------------------------------------------------------- */
export function CanvasCard({
  map,
  stations,
  loading,
  onEditStation,
  className,
  canvasClassName,
}: {
  map: MapSummaryDto | null;
  stations: StationDto[];
  loading: boolean;
  onEditStation?: (id: string) => void;
  className?: string;
  canvasClassName?: string;
}) {
  const [hoverId, setHoverId] = useState<string | null>(null);
  const [pinned, setPinned] = useState<string | null>(null);
  const [cursor, setCursor] = useState<{ x: number; y: number } | null>(null);
  const [hoverRobotId, setHoverRobotId] = useState<string | null>(null);
  // True browser fullscreen via the Fullscreen API — covers browser chrome
  // (tabs, address bar) too, not just an in-page overlay. The card ref is
  // the element we request fullscreen on; `fullscreenchange` is the source
  // of truth for the boolean so ESC / F11 / browser UI all stay in sync.
  const cardRef = useRef<HTMLDivElement>(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
  useEffect(() => {
    const onChange = () => {
      setIsFullscreen(document.fullscreenElement === cardRef.current);
    };
    document.addEventListener("fullscreenchange", onChange);
    return () => document.removeEventListener("fullscreenchange", onChange);
  }, []);
  const toggleFullscreen = useCallback(() => {
    const el = cardRef.current;
    if (!el) return;
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(() => {});
    } else {
      el.requestFullscreen().catch(() => {});
    }
  }, []);
  // Rotation — clockwise in 45° steps, cycles 0→45→…→315→0. Applied as a CSS
  // transform on the SVG; gesture handlers convert in/out of the rotated
  // frame via the screenToSvg / rotateDelta / svgToScreen helpers above.
  // The ref keeps stable callbacks (beginGesture is useCallback([])) from
  // reading a stale rotation between gesture frames.
  const [rotation, setRotation] = useState(0);
  const rotationRef = useRef(rotation);
  rotationRef.current = rotation;
  const rotateCw = useCallback(() => setRotation((r) => (r + 45) % 360), []);
  // Container ref — the wrapper div that holds the SVG. The container is
  // NEVER rotated, so its getBoundingClientRect() is the stable rectangle we
  // do all px↔world math against (the SVG's own rect is the rotated bbox).
  const canvasContainerRef = useRef<HTMLDivElement>(null);
  // Operation-instructions popup. Click outside or press ESC to dismiss.
  // The wrapper ref is the trigger button + popup pair so we can ignore
  // clicks that originated inside either of them.
  const [showInstructions, setShowInstructions] = useState(false);
  const helpRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!showInstructions) return;
    const onClick = (e: MouseEvent) => {
      if (helpRef.current && !helpRef.current.contains(e.target as Node)) {
        setShowInstructions(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setShowInstructions(false);
    };
    document.addEventListener("mousedown", onClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onClick);
      document.removeEventListener("keydown", onKey);
    };
  }, [showInstructions]);

  // Live robot positions for this map. The hook handles polling,
  // visibility-change pausing, and abort on map switch / unmount.
  const { positions: robots, lastTickMs } = useRobotPositions(map?.id ?? null);
  // Force a re-render every 30 s so the "X s ago" indicator stays current
  // even when no robots arrive (e.g. RIOT3 is reachable but empty).
  const [, forceTick] = useState(0);
  useEffect(() => {
    const t = window.setInterval(() => forceTick((n) => (n + 1) % 1_000), 30_000);
    return () => window.clearInterval(t);
  }, []);
  // Measured SVG width in CSS pixels — drives the world-unit ↔ screen-px
  // conversion so dot/halo/stroke sizes stay constant on screen no matter
  // how big the world coordinate range is. Without this, a 72 m × 69 m map
  // turns every dot into a 600 mm blob that hides 1–2 m aisle spacing.
  const svgRef = useRef<SVGSVGElement>(null);
  const [svgWidth, setSvgWidth] = useState(800);
  useEffect(() => {
    if (!svgRef.current) return;
    const el = svgRef.current;
    const update = () => {
      // clientWidth is the un-transformed layout width; getBoundingClientRect()
      // would return the rotated bbox (larger when rotation isn't a multiple
      // of 90°) and skew the px↔world ratio that drives dot sizing.
      const w = el.clientWidth;
      if (w > 0) setSvgWidth(w);
    };
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // Compute the world bounds across BOTH stations and live robots so a robot
  // driving outside the station cluster doesn't disappear off-canvas. 8%
  // padding so points never sit on the frame edge.
  const bounds = useMemo(() => {
    const points: { x: number; y: number }[] = [];
    for (const s of stations) points.push({ x: s.x, y: s.y });
    for (const r of robots) points.push({ x: r.x, y: r.y });
    if (points.length === 0) {
      const W = map?.width ?? 1000;
      const H = map?.height ?? 700;
      return { minX: 0, minY: 0, maxX: W, maxY: H };
    }
    const xs = points.map((p) => p.x);
    const ys = points.map((p) => p.y);
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
  }, [stations, robots, map]);

  const viewW = bounds.maxX - bounds.minX;
  const viewH = bounds.maxY - bounds.minY;
  // Zoom limits as factors of the natural span so they're invariant to whatever
  // unit (mm/cm/m/grid) the world coords happen to use. Shared by button,
  // wheel, and pinch gates — keep these in sync with the chip readout, which
  // displays viewW / view.w as the current factor.
  const MAX_ZOOM_IN = 50;
  const MAX_ZOOM_OUT = 4;

  // Viewport — the slice of world space currently rendered. Mutated by wheel
  // (zoom) and drag (pan); reset to fit-all on map change or "recenter" press.
  // Card aspect ratio still uses the natural (viewW × viewH) so the card itself
  // doesn't reflow on zoom.
  const [view, setView] = useState({
    x: bounds.minX,
    y: bounds.minY,
    w: viewW,
    h: viewH,
  });
  // View-fit policy:
  //  • Before the user touches the canvas, track bounds — covers the
  //    common case where stations load *after* the map prop arrives, so
  //    the initial fallback bounds (1000×700) don't strand `view` at the
  //    zoom-out cap once real station coords show up.
  //  • Once the user has panned/pinched/zoomed/wheeled, freeze against
  //    bounds shifts so a robot driving outside the cluster (which
  //    expands bounds) doesn't yank the camera mid-inspection.
  // Recenter / map switch both clear the lock so fit-all resumes.
  const lastMapIdRef = useRef<string | null>(null);
  const hasInteractedRef = useRef(false);
  useEffect(() => {
    if (!map?.id) return;
    if (lastMapIdRef.current !== map.id) {
      lastMapIdRef.current = map.id;
      hasInteractedRef.current = false;
    }
    if (hasInteractedRef.current) return;
    setView({ x: bounds.minX, y: bounds.minY, w: viewW, h: viewH });
  }, [map?.id, bounds.minX, bounds.minY, viewW, viewH]);

  // Step picker — drives the dashed map-boundary rhythm so the dash period
  // scales with zoom level instead of pixel-locking.
  const stepBase = Math.max(view.w, view.h) / 6;
  const magnitude = Math.pow(10, Math.floor(Math.log10(stepBase)));
  const normalized = stepBase / magnitude;
  const step =
    magnitude *
    (normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10);

  // Pointer-based gesture state — unifies mouse, touch, and stylus.
  //  • 1 pointer  → pan (translate the view)
  //  • 2 pointers → pinch-zoom around the gesture midpoint + translate
  // dragMoved flips true once movement exceeds CLICK_THRESHOLD so the station
  // onClick can ignore a click that was actually the end of a pan/pinch.
  // viewRef shadows view so the window-level pointer listener can read the
  // latest view without re-binding on every pan-induced render.
  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const panStart = useRef<{ cx: number; cy: number; vx: number; vy: number } | null>(null);
  const pinchStart = useRef<{
    dist: number;
    rectW: number;
    rectH: number;
    midPx: number;
    midPy: number;
    view: { x: number; y: number; w: number; h: number };
  } | null>(null);
  const dragMoved = useRef(false);
  const [isGesturing, setIsGesturing] = useState(false);
  const viewRef = useRef(view);
  viewRef.current = view;
  const CLICK_THRESHOLD = 6; // a bit looser than mouse to absorb finger jitter

  const beginGesture = useCallback((e: React.PointerEvent<SVGSVGElement>) => {
    // Ignore non-primary mouse buttons; touch + pen don't have buttons here.
    if (e.pointerType === "mouse" && e.button !== 0) return;
    const container = canvasContainerRef.current;
    if (!container) return;
    pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
    const v = viewRef.current;

    if (pointers.current.size === 2) {
      const pts = [...pointers.current.values()];
      const rect = container.getBoundingClientRect();
      const rot = rotationRef.current;
      const dist = Math.hypot(pts[1]!.x - pts[0]!.x, pts[1]!.y - pts[0]!.y);
      // Snapshot the midpoint in un-rotated SVG space so the world-anchor
      // math below sees a stable, rotation-agnostic position.
      const midScreenX = (pts[0]!.x + pts[1]!.x) / 2;
      const midScreenY = (pts[0]!.y + pts[1]!.y) / 2;
      const mid = screenToSvg(midScreenX, midScreenY, rect, rot);
      pinchStart.current = {
        dist: Math.max(dist, 1),
        rectW: rect.width,
        rectH: rect.height,
        midPx: mid.x,
        midPy: mid.y,
        view: { x: v.x, y: v.y, w: v.w, h: v.h },
      };
      panStart.current = null;
      dragMoved.current = true; // never treat a pinch as a tap
      hasInteractedRef.current = true;
    } else if (pointers.current.size === 1) {
      panStart.current = { cx: e.clientX, cy: e.clientY, vx: v.x, vy: v.y };
      dragMoved.current = false;
    }
    setIsGesturing(true);
  }, []);

  // Window-level move/up listeners — keep panning alive even when the pointer
  // leaves the SVG, and avoid setPointerCapture so synthetic click events
  // still target station <g> nodes after a tap.
  useEffect(() => {
    if (!isGesturing) return;
    const onMove = (e: PointerEvent) => {
      if (!pointers.current.has(e.pointerId)) return;
      pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
      const container = canvasContainerRef.current;
      if (!container) return;
      const rect = container.getBoundingClientRect();
      const rot = rotationRef.current;

      // Pinch-zoom: rescale around the original midpoint, then shift by
      // current midpoint so 2-finger drag pans + zooms in one motion.
      if (pointers.current.size === 2 && pinchStart.current) {
        const pts = [...pointers.current.values()];
        const dist = Math.hypot(pts[1]!.x - pts[0]!.x, pts[1]!.y - pts[0]!.y);
        // Convert current midpoint to un-rotated SVG space so it can be
        // compared apples-to-apples against pinchStart.midPx/Py.
        const mid = screenToSvg(
          (pts[0]!.x + pts[1]!.x) / 2,
          (pts[0]!.y + pts[1]!.y) / 2,
          rect,
          rot,
        );
        const ps = pinchStart.current;
        const scale = ps.dist / Math.max(dist, 1);
        const newW = ps.view.w * scale;
        const newH = ps.view.h * scale;
        if (newW < viewW / MAX_ZOOM_IN || newH < viewH / MAX_ZOOM_IN) return;
        if (newW > viewW * MAX_ZOOM_OUT || newH > viewH * MAX_ZOOM_OUT) return;
        // Anchor the gesture midpoint in data-space so the pinch zooms
        // around what's under the user's fingers. Y is flipped (see wheel
        // handler for the same formula): top of screen = high data-Y.
        const wx = ps.view.x + (ps.midPx / ps.rectW) * ps.view.w;
        const wy = ps.view.y + (1 - ps.midPy / ps.rectH) * ps.view.h;
        setView({
          x: wx - (mid.x / rect.width) * newW,
          y: wy - (1 - mid.y / rect.height) * newH,
          w: newW,
          h: newH,
        });
        return;
      }

      // Single-pointer pan.
      const ps = panStart.current;
      if (!ps) return;
      const dxScreen = e.clientX - ps.cx;
      const dyScreen = e.clientY - ps.cy;
      if (Math.abs(dxScreen) + Math.abs(dyScreen) > CLICK_THRESHOLD) {
        dragMoved.current = true;
        hasInteractedRef.current = true;
      }
      // Rotate the screen delta into the un-rotated SVG frame so dragging
      // a finger right moves the viewport "screen-right" regardless of
      // the current rotation step.
      const { dx: dxPx, dy: dyPx } = rotateDelta(dxScreen, dyScreen, rot);
      setView((v) => ({
        ...v,
        x: ps.vx - (dxPx / rect.width) * v.w,
        // Y-flip frame: view.y is the data-Y *lower* bound (visual bottom).
        // Drag-down (Google-Maps style) should move the viewport up in data
        // space — i.e. increase view.y — so the cursor sticks to the same
        // world point under the finger.
        y: ps.vy + (dyPx / rect.height) * v.h,
      }));
    };
    const onUp = (e: PointerEvent) => {
      pointers.current.delete(e.pointerId);
      if (pointers.current.size < 2) pinchStart.current = null;
      if (pointers.current.size === 0) {
        panStart.current = null;
        setIsGesturing(false);
      } else if (pointers.current.size === 1) {
        // Re-anchor pan to the remaining finger after a pinch lift-off so
        // the next pointermove doesn't jump by the pinch's accumulated delta.
        const pt = [...pointers.current.values()][0]!;
        const v = viewRef.current;
        panStart.current = { cx: pt.x, cy: pt.y, vx: v.x, vy: v.y };
        dragMoved.current = true;
      }
    };
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onUp);
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);
    };
  }, [isGesturing, viewW, viewH]);

  // Wheel zoom — multiplicative around the cursor. Attached natively because
  // React's synthetic onWheel is registered as a passive listener, which
  // would block our preventDefault() (the page would scroll behind the map).
  useEffect(() => {
    const el = svgRef.current;
    if (!el) return;
    const handler = (e: WheelEvent) => {
      e.preventDefault();
      const container = canvasContainerRef.current;
      if (!container) return;
      hasInteractedRef.current = true;
      const rect = container.getBoundingClientRect();
      // Convert cursor into un-rotated SVG space so the zoom anchors on
      // whatever's visually under the wheel pointer, even when rotated.
      const { x: px, y: py } = screenToSvg(e.clientX, e.clientY, rect, rotationRef.current);
      setView((v) => {
        const factor = e.deltaY > 0 ? 1.2 : 1 / 1.2;
        const newW = v.w * factor;
        const newH = v.h * factor;
        if (newW < viewW / MAX_ZOOM_IN || newH < viewH / MAX_ZOOM_IN) return v;
        if (newW > viewW * MAX_ZOOM_OUT || newH > viewH * MAX_ZOOM_OUT) return v;
        // X is straightforward: wx is the data-X under the cursor.
        // Y is Y-flipped: at cursor screen position py/rect, the visible
        // *data* Y is view.y + (1 - py/rect)*view.h (top of screen = high Y).
        // Anchor that data-Y under the cursor when we resize the viewport.
        const wx = v.x + (px / rect.width) * v.w;
        const wy = v.y + (1 - py / rect.height) * v.h;
        return {
          x: wx - (px / rect.width) * newW,
          y: wy - (1 - py / rect.height) * newH,
          w: newW,
          h: newH,
        };
      });
    };
    el.addEventListener("wheel", handler, { passive: false });
    return () => el.removeEventListener("wheel", handler);
  }, [viewW, viewH]);

  const zoomBy = useCallback(
    (factor: number) => {
      hasInteractedRef.current = true;
      setView((v) => {
        const newW = v.w * factor;
        const newH = v.h * factor;
        if (newW < viewW / MAX_ZOOM_IN || newH < viewH / MAX_ZOOM_IN) return v;
        if (newW > viewW * MAX_ZOOM_OUT || newH > viewH * MAX_ZOOM_OUT) return v;
        // Anchor at viewport center so button-driven zoom feels balanced.
        const cx = v.x + v.w / 2;
        const cy = v.y + v.h / 2;
        return { x: cx - newW / 2, y: cy - newH / 2, w: newW, h: newH };
      });
    },
    [viewW, viewH],
  );

  const recenter = useCallback(() => {
    hasInteractedRef.current = false;
    setView({ x: bounds.minX, y: bounds.minY, w: viewW, h: viewH });
  }, [bounds.minX, bounds.minY, viewW, viewH]);

  // Double-tap recenter — covers touch where native `dblclick` is unreliable.
  // Mouse still uses `onDoubleClick` on the SVG (handled separately).
  const lastTap = useRef<{ t: number; x: number; y: number } | null>(null);
  const onTapEnd = useCallback(
    (e: React.PointerEvent<SVGSVGElement>) => {
      if (e.pointerType !== "touch") return;
      if (dragMoved.current) return;
      const now = e.timeStamp;
      const prev = lastTap.current;
      if (
        prev &&
        now - prev.t < 350 &&
        Math.hypot(e.clientX - prev.x, e.clientY - prev.y) < 30
      ) {
        recenter();
        lastTap.current = null;
      } else {
        lastTap.current = { t: now, x: e.clientX, y: e.clientY };
      }
    },
    [recenter],
  );

  const active = pinned ?? hoverId;
  const activeStation = stations.find((s) => s.id === active) ?? null;

  // World units per screen pixel. Stays valid even if pxPerWorld is small
  // (large maps) — `r` in world space then maps back to a constant on-screen
  // size, so adjacent stations never overlap when their real spacing > a few
  // pixels apart. Driven off the current viewport so zoom recomputes radii.
  const pxPerWorld = svgWidth > 0 ? svgWidth / view.w : 1;
  const worldPerPx = 1 / pxPerWorld;
  // Tuned for "small enough to inspect 1 m aisle spacing, big enough to click".
  const DOT_PX = 5;
  const RING_PX = 11;
  const HALO_PX = 22;
  const STROKE_PX = 1.2;
  const r = DOT_PX * worldPerPx;
  const ringR = RING_PX * worldPerPx;
  const haloR = HALO_PX * worldPerPx;
  const sw = STROKE_PX * worldPerPx;
  const sw2 = 1.5 * worldPerPx;
  // Zoom feedback signals — used by LOD label visibility and could drive
  // any future zoom-tiered overlays (footprints, labels, etc).
  //   labelOpacity: hidden at fit-all, fades in 2×→4× so dense clusters
  //   don't bloom into a wall of text the moment the user starts zooming.
  const zoomFactor = view.w > 0 ? viewW / view.w : 1;
  const labelOpacity = Math.max(0, Math.min(1, (zoomFactor - 2) / 2));

  return (
    <GlassCard
      ref={cardRef}
      variant="strong"
      className={cn(
        "overflow-hidden",
        // In browser fullscreen the UA stylesheet sizes the element to the
        // whole screen — we just need flex layout + a solid bg (the default
        // :fullscreen bg is black, which would bleed around the glass).
        isFullscreen
          ? "flex flex-col rounded-none !bg-[var(--color-canvas)]"
          : className,
      )}
    >
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
                <span>
                  extent · {Math.round(map.width).toLocaleString()} × {Math.round(map.height).toLocaleString()}
                </span>
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

      {/* Canvas height fills the viewport so the map is the focal point of
          the page: 85svh on a typical desktop, clamped to 28rem floor (small
          phones) and 72rem ceiling (giant 4K monitors). Percent-based scales
          predictably across screen sizes. The SVG inside uses
          preserveAspectRatio="xMidYMid meet", so the square world content
          stays centered — wider screens just get letterboxed margins, never
          a stretched cartography. */}
      <div
        ref={canvasContainerRef}
        className={cn(
          "relative",
          isFullscreen
            ? "flex-1 min-h-0"
            : canvasClassName ?? "h-[clamp(28rem,85svh,72rem)]",
        )}
      >
        {!loading && stations.length === 0 && <CanvasEmptyState />}

        <svg
          ref={svgRef}
          role="img"
          aria-label="Station topology"
          viewBox={`${view.x} ${view.y} ${view.w} ${view.h}`}
          preserveAspectRatio="xMidYMid meet"
          className="absolute inset-0 h-full w-full select-none"
          style={{
            cursor: isGesturing ? "grabbing" : "grab",
            touchAction: "none",
            transform: `rotate(${rotation}deg)`,
            transformOrigin: "center center",
            transition: "transform 0.4s cubic-bezier(0.22, 1, 0.36, 1)",
          }}
          onPointerDown={beginGesture}
          onPointerUp={onTapEnd}
          onDoubleClick={recenter}
          onPointerMove={(e) => {
            // Cursor read-out follows the mouse only; touch has no hover.
            if (e.pointerType === "touch") return;
            const container = canvasContainerRef.current;
            if (!container) return;
            const rect = container.getBoundingClientRect();
            // Convert into un-rotated SVG px first so the world-coord mapping
            // below sees the same position regardless of rotation step.
            const { x: sx, y: sy } = screenToSvg(e.clientX, e.clientY, rect, rotation);
            const localX = (sx / rect.width) * view.w + view.x;
            const localY = (sy / rect.height) * view.h + view.y;
            // SVG coord → data coord. The data layer is Y-flipped, so the data
            // y under the cursor is the mirror of localY about the viewport
            // midline (= 2*view.y + view.h - localY).
            const dataY = 2 * view.y + view.h - localY;
            setCursor({ x: localX, y: dataY });
          }}
          onPointerLeave={(e) => {
            if (e.pointerType === "touch") return;
            setCursor(null);
            setHoverId(null);
          }}
        >
          <defs>
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

          {/* Y-flip wrapper — RIOT3 (and robotics convention) uses Y-up while
              SVG default is Y-down. We render all spatial data inside this
              group so a station at world y=4,000 actually appears *above*
              a station at y=10,000 on screen. Axis text labels stay outside
              this group so the characters don't render upside-down — their
              y positions get the same flip math applied below. */}
          <g
            transform={`translate(0 ${2 * view.y + view.h}) scale(1 -1)`}
          >
          {/* World-unit grid — lines spaced at `step` (same rhythm as the
              dashed boundary) so the grid spreads visibly when the user
              zooms. Pixel-locked station markers stay constant on screen,
              and this layer carries the "I am zooming" feedback that
              cartographic best practice asks of map content (not the
              markers themselves). Subtle ink — should register motion
              without competing with stations or robots. */}
          <g
            stroke="var(--color-ink-200)"
            strokeWidth={1 * worldPerPx}
            opacity={0.4}
            pointerEvents="none"
          >
            {(() => {
              const lines: React.ReactElement[] = [];
              const x0 = Math.ceil(view.x / step) * step;
              const x1 = view.x + view.w;
              const y0 = Math.ceil(view.y / step) * step;
              const y1 = view.y + view.h;
              for (let x = x0; x <= x1; x += step) {
                lines.push(
                  <line key={`gv-${x}`} x1={x} y1={view.y} x2={x} y2={y1} />,
                );
              }
              for (let y = y0; y <= y1; y += step) {
                lines.push(
                  <line key={`gh-${y}`} x1={view.x} y1={y} x2={x1} y2={y} />,
                );
              }
              return lines;
            })()}
          </g>
          {/* Stations — pulse for active, dim for inactive, ring for offline override.
              r/ringR/haloR/sw are world-unit values computed from the measured
              screen-px target so dot size stays constant regardless of map extent. */}
          {stations.map((s, i) => {
            const k = typeKey(s.type);
            const meta = TYPE_META[k];
            const isActiveDot = s.id === active;
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
                onClick={(e) => {
                  // Ignore the click if the user just finished a pan gesture.
                  if (dragMoved.current) return;
                  e.stopPropagation();
                  setPinned((id) => (id === s.id ? null : s.id));
                }}
                className="cursor-pointer"
              >
                {/* Soft halo — only on active dots, kept subtle so dense
                    aisles don't blur into a single blob. */}
                {s.isActive && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={haloR}
                    fill={`url(#station-glow-${k})`}
                    opacity={0.55}
                  />
                )}

                {/* Breathing ring on active */}
                {s.isActive && (
                  <motion.circle
                    cx={s.x}
                    cy={s.y}
                    r={ringR}
                    fill="none"
                    stroke={meta.swatch}
                    strokeOpacity={0.35}
                    strokeWidth={sw2}
                    initial={{ opacity: 0.55, scale: 0.85 }}
                    animate={{
                      opacity: [0.55, 0, 0.55],
                      scale: [0.85, 1.5, 0.85],
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
                    r={ringR * 1.1}
                    fill="none"
                    stroke={meta.swatch}
                    strokeWidth={sw * 1.6}
                    strokeOpacity={0.95}
                  />
                )}

                {/* Manual-offline override */}
                {s.isManualOverrideActive && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={ringR}
                    fill="none"
                    stroke="var(--color-amber)"
                    strokeWidth={sw}
                    strokeDasharray={`${r * 0.8} ${r * 0.6}`}
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
                  strokeWidth={sw2 * 0.6}
                />

                {/* Inactive cross */}
                {!s.isActive && (
                  <g
                    stroke="white"
                    strokeOpacity={0.85}
                    strokeWidth={sw2 * 0.6}
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

                {/* LOD label — station code below the dot, fades in once
                    the user has zoomed past 2× so dense fit-all clusters
                    aren't drowned in text. Baseline sits BELOW the halo
                    (haloR = 22 px) plus a breathing-room margin so glyphs
                    don't render on top of the soft glow. Inner scale(1 -1)
                    undoes the parent Y-flip so glyphs render upright;
                    pixel-locked fontSize keeps the label the same screen
                    size at every zoom. */}
                {labelOpacity > 0 && s.code && (
                  <g
                    transform={`translate(${s.x} ${s.y}) scale(1 -1)`}
                    pointerEvents="none"
                  >
                    <text
                      x={0}
                      y={haloR + 10 * worldPerPx}
                      fontSize={11 * worldPerPx}
                      textAnchor="middle"
                      fill="var(--color-ink-700)"
                      stroke="white"
                      strokeWidth={3 * worldPerPx}
                      strokeOpacity={0.85}
                      paintOrder="stroke"
                      opacity={labelOpacity}
                      style={{ fontWeight: 600 }}
                    >
                      {s.code}
                    </text>
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
              r={3 * worldPerPx}
              fill="var(--color-ink-700)"
              fillOpacity={0.45}
            />
          </g>

          {/* Live robot layer — sits between stations and vignette so robots
              draw on top of the topology but the corner vignette still
              applies. Rendered in the same flipped Y space as everything
              else, so its world coords match station coords. */}
          <RobotLayer
            positions={robots}
            worldPerPx={worldPerPx}
            hoverId={hoverRobotId}
            onHover={setHoverRobotId}
          />

          </g>{/* end Y-flip wrapper */}
        </svg>

        {/* Robot tooltip — HTML overlay positioned in screen space from the
            hovered robot's world coords. The data layer is Y-flipped, so the
            robot at data y is rendered at SVG y = 2*view.y + view.h - y;
            screen-px conversion mirrors that. */}
        {(() => {
          const hovered = robots.find((r) => r.deviceKey === hoverRobotId);
          if (!hovered) return null;
          const container = canvasContainerRef.current;
          if (!container) return null;
          const rect = container.getBoundingClientRect();
          // Robot world coord → un-rotated SVG px → screen px (forward rotation).
          // The tooltip is absolutely positioned inside the container, so left/top
          // are relative to it (not the page).
          const svgPx = ((hovered.x - view.x) / view.w) * rect.width;
          const svgPy = ((view.y + view.h - hovered.y) / view.h) * rect.height;
          const { x: left, y: top } = svgToScreen(svgPx, svgPy, rect, rotation);
          return <RobotTooltip robot={hovered} left={left} top={top} />;
        })()}

        {/* Live indicator — top-left chip above the canvas grid. Shows tick
            age + robot count so operators can sanity-check the stream. */}
        <div className="pointer-events-none absolute top-4 left-4 flex items-center gap-1.5 rounded-full glass px-2.5 py-1 text-[10.5px] font-mono tracking-tight text-[var(--color-ink-700)]">
          <StatusPulse tone={lastTickMs ? "success" : "amber"} />
          <span>
            {robots.length} robot{robots.length === 1 ? "" : "s"}
          </span>
          {lastTickMs && (
            <span className="text-[var(--color-ink-400)]">
              · {((performance.now() - lastTickMs) / 1000).toFixed(0)}s
            </span>
          )}
        </div>

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

        {/* Zoom controls — bottom-right floating stack. Buttons round-trip
            through the same zoomBy/recenter handlers as wheel + double-click,
            so all three input methods stay in sync. The zoom-level chip is a
            quick way to confirm "how zoomed am I" at a glance. */}
        <div className="pointer-events-none absolute bottom-4 right-4 flex flex-col items-end gap-1.5">
          <span className="pointer-events-auto inline-flex items-center gap-1 rounded-full glass px-2.5 py-1 text-[10.5px] font-mono tabular-nums tracking-tight text-[var(--color-ink-600)]">
            {(viewW / view.w).toFixed(2)}×
          </span>
          {/* helpRef wraps the stack + the popup so clicks on either count as
              "inside" — the click-outside listener only dismisses on clicks
              outside this wrapper. The popup is a sibling of the stack so it
              escapes the stack's overflow-hidden clipping. */}
          <div ref={helpRef} className="pointer-events-auto relative">
          <div className="flex flex-col rounded-[16px] glass-strong overflow-hidden">
            <button
              type="button"
              aria-label="Zoom in"
              onClick={() => zoomBy(1 / 1.4)}
              className="grid h-10 w-10 place-items-center text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer transition-colors"
            >
              <Plus className="h-4 w-4" strokeWidth={2.4} />
            </button>
            <span aria-hidden className="h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06]" />
            <button
              type="button"
              aria-label="Zoom out"
              onClick={() => zoomBy(1.4)}
              className="grid h-10 w-10 place-items-center text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer transition-colors"
            >
              <Minus className="h-4 w-4" strokeWidth={2.4} />
            </button>
            <span aria-hidden className="h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06]" />
            <button
              type="button"
              aria-label="Recenter"
              onClick={recenter}
              className="grid h-10 w-10 place-items-center text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer transition-colors"
            >
              <Maximize2 className="h-4 w-4" strokeWidth={2.2} />
            </button>
            <span aria-hidden className="h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06]" />
            {/* Rotate map — clockwise 45° per click. Shows the current angle as
                a tiny readout inside the tooltip so the user can tell where in
                the 8-step cycle they are without counting clicks. */}
            <button
              type="button"
              aria-label={`Rotate map clockwise (currently ${rotation}°)`}
              onClick={rotateCw}
              className="group/rt relative grid h-10 w-10 place-items-center text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer transition-colors"
            >
              <RotateCw className="h-4 w-4" strokeWidth={2.4} />
              <span
                role="tooltip"
                className="pointer-events-none absolute right-full mr-2 whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white opacity-0 translate-x-1 transition-all duration-200 group-hover/rt:opacity-100 group-hover/rt:translate-x-0"
              >
                Rotate{rotation > 0 ? ` · ${rotation}°` : ""}
              </span>
            </button>
            <span aria-hidden className="h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06]" />
            {/* Full-screen toggle — Maximize icon to enter, Minimize to exit.
                Hover tooltip pins to the LEFT so it doesn't get clipped by the
                viewport edge (stack sits at bottom-right of the canvas). */}
            <button
              type="button"
              aria-label={isFullscreen ? "Exit full-screen" : "Enter full-screen"}
              aria-pressed={isFullscreen}
              onClick={toggleFullscreen}
              className="group/fs relative grid h-10 w-10 place-items-center text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer transition-colors"
            >
              {isFullscreen ? (
                <Minimize className="h-4 w-4" strokeWidth={2.4} />
              ) : (
                <Maximize className="h-4 w-4" strokeWidth={2.4} />
              )}
              <span
                role="tooltip"
                className="pointer-events-none absolute right-full mr-2 whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white opacity-0 translate-x-1 transition-all duration-200 group-hover/fs:opacity-100 group-hover/fs:translate-x-0"
              >
                {isFullscreen ? "Exit full-screen" : "Full-screen"}
              </span>
            </button>
            <span aria-hidden className="h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06]" />
            {/* Operation instructions — click toggle. Tooltip hides while the
                popup is open so the two never overlap. */}
            <button
              type="button"
              aria-label={showInstructions ? "Hide operation instructions" : "Show operation instructions"}
              aria-pressed={showInstructions}
              onClick={() => setShowInstructions((v) => !v)}
              className="group/hp relative grid h-10 w-10 place-items-center text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer transition-colors"
            >
              <HelpCircle className="h-4 w-4" strokeWidth={2.2} />
              {!showInstructions && (
                <span
                  role="tooltip"
                  className="pointer-events-none absolute right-full mr-2 whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white opacity-0 translate-x-1 transition-all duration-200 group-hover/hp:opacity-100 group-hover/hp:translate-x-0"
                >
                  Operation instructions
                </span>
              )}
            </button>
          </div>
          {/* Operation-instructions popup — sibling of the stack so the stack's
              overflow-hidden doesn't clip it. Anchors to the bottom-right of
              the wrapper so the corners line up with the help button. */}
          <AnimatePresence>
            {showInstructions && (
              <motion.div
                key="ops-popover"
                initial={{ opacity: 0, scale: 0.96, x: 8 }}
                animate={{ opacity: 1, scale: 1, x: 0 }}
                exit={{ opacity: 0, scale: 0.96, x: 8 }}
                transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
                role="dialog"
                aria-label="Operation instructions"
                className="absolute bottom-0 right-full mr-2 w-[320px] rounded-[18px] glass-strong p-4 shadow-[0_30px_60px_-20px_rgba(15,23,42,0.45)]"
              >
                <OperationInstructions
                  onClose={() => setShowInstructions(false)}
                />
              </motion.div>
            )}
          </AnimatePresence>
          </div>
          <span className="pointer-events-none rounded-full glass px-2.5 py-1 text-[10px] tracking-tight text-[var(--color-ink-500)] hidden md:inline-block">
            scroll / pinch · drag · 2× tap
          </span>
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
              {onEditStation && (
                <button
                  type="button"
                  onClick={() => onEditStation(activeStation.id)}
                  className="mt-3 inline-flex w-full items-center justify-center gap-1.5 h-8 rounded-full bg-[var(--color-brand-900)] text-white text-[11.5px] font-semibold tracking-tight cursor-pointer hover:-translate-y-px transition-transform shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_8px_18px_-10px_rgba(14,21,48,0.55)] dark:bg-[var(--color-brand-500)]"
                >
                  <Pencil className="h-3 w-3" strokeWidth={2.5} />
                  Edit station
                </button>
              )}
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </GlassCard>
  );
}

/* -------------------------------------------------------------------------- */
/* OperationInstructions — popover content explaining mouse + touch gestures. */
/* Two columns side-by-side (mouse / touch) for the gesture-driven actions,   */
/* a separator, then a short list of button-only actions. Click-outside /     */
/* ESC dismissal lives in the parent (CanvasCard) so this body can stay pure. */
/* -------------------------------------------------------------------------- */
function OperationInstructions({ onClose }: { onClose: () => void }) {
  return (
    <>
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <span className="grid h-7 w-7 place-items-center rounded-[10px] bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]">
            <HelpCircle className="h-3.5 w-3.5" strokeWidth={2.2} />
          </span>
          <h3 className="font-display text-[13px] font-semibold tracking-tight text-[var(--color-ink-900)]">
            Operation instructions
          </h3>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close"
          className="grid h-6 w-6 place-items-center rounded-full text-[var(--color-ink-500)] hover:bg-white/60 dark:hover:bg-white/[0.06] cursor-pointer"
        >
          <X className="h-3 w-3" strokeWidth={2.4} />
        </button>
      </div>

      {/* Column headers — Mouse vs Touch */}
      <div className="grid grid-cols-[1fr_auto_auto] gap-x-3 gap-y-1.5 items-center">
        <span />
        <span className="flex items-center justify-end gap-1 text-[9.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-500)]">
          <Mouse className="h-3 w-3" strokeWidth={2.2} />
          Mouse
        </span>
        <span className="flex items-center justify-end gap-1 text-[9.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-500)]">
          <Hand className="h-3 w-3" strokeWidth={2.2} />
          Touch
        </span>

        <span className="col-span-3 h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06] my-1" />

        <span className="text-[12px] font-medium text-[var(--color-ink-800)]">Move</span>
        <Kbd>Drag</Kbd>
        <Kbd>Drag</Kbd>

        <span className="text-[12px] font-medium text-[var(--color-ink-800)]">Zoom</span>
        <span className="flex items-center justify-end gap-1">
          <Kbd>Ctrl</Kbd>
          <span className="text-[10px] text-[var(--color-ink-500)]">+</span>
          <Kbd>Scroll</Kbd>
        </span>
        <Kbd>Pinch</Kbd>

        <span className="text-[12px] font-medium text-[var(--color-ink-800)]">Back to origin</span>
        <Kbd>Double click</Kbd>
        <Kbd>Double tap</Kbd>
      </div>

      {/* Button-only actions — single shared instruction, full-width row */}
      <div className="mt-3 pt-3 border-t border-[var(--color-ink-100)] dark:border-white/[0.06] space-y-2">
        <ButtonRow icon={<RotateCw className="h-3 w-3" strokeWidth={2.4} />} label="Rotate" />
        <ButtonRow icon={<Maximize className="h-3 w-3" strokeWidth={2.4} />} label="Full-screen" />
      </div>
    </>
  );
}

function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <kbd className="inline-flex items-center justify-center rounded-md border border-[var(--color-ink-200)] bg-white px-1.5 py-0.5 text-[10px] font-mono font-semibold tracking-tight text-[var(--color-ink-700)] shadow-[0_1px_0_rgba(15,23,42,0.04)] dark:border-white/[0.08] dark:bg-white/[0.06] dark:text-[var(--color-ink-700)] whitespace-nowrap">
      {children}
    </kbd>
  );
}

function ButtonRow({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <div className="flex items-center justify-between text-[11.5px]">
      <span className="font-medium text-[var(--color-ink-800)]">{label}</span>
      <span className="flex items-center gap-1.5 text-[var(--color-ink-500)]">
        Click
        <span className="grid h-5 w-5 place-items-center rounded-md bg-[var(--color-ink-100)] text-[var(--color-ink-700)] dark:bg-white/[0.08]">
          {icon}
        </span>
        button
      </span>
    </div>
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
/* StationDirectory — searchable, filter-pillable table of all stations.       */
/* -------------------------------------------------------------------------- */
function StationDirectory({
  stations,
  loading,
  error,
  onEditStation,
}: {
  stations: StationDto[];
  loading: boolean;
  error: string | null;
  onEditStation: (id: string) => void;
}) {
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState<TypeKey | "ALL">("ALL");
  const [activeOnly, setActiveOnly] = useState(false);

  // Per-type counts feed the filter chips so the operator sees the
  // distribution at a glance — replaces the standalone Taxonomy card.
  const counts = useMemo(() => {
    const m: Partial<Record<TypeKey, number>> = {};
    for (const s of stations) {
      const k = typeKey(s.type);
      m[k] = (m[k] ?? 0) + 1;
    }
    return m;
  }, [stations]);

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
            count={stations.length}
          >
            All
          </TypeChip>
          {TYPE_KEYS.map((k) => (
            <TypeChip
              key={k}
              active={typeFilter === k}
              onClick={() => setTypeFilter(typeFilter === k ? "ALL" : k)}
              color={TYPE_META[k].swatch}
              count={counts[k] ?? 0}
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
                  <th className="py-2.5 pr-5 font-semibold sr-only">Actions</th>
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
                      <td className="py-3">
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
                      <td className="py-3 pr-5 text-right">
                        <button
                          type="button"
                          onClick={() => onEditStation(s.id)}
                          className="inline-flex items-center gap-1 rounded-full bg-white/65 dark:bg-white/[0.05] px-2.5 h-7 text-[10.5px] font-semibold tracking-tight text-[var(--color-ink-800)] hover:bg-white hover:-translate-y-px transition-all cursor-pointer"
                          aria-label={`Edit ${s.name}`}
                        >
                          <Pencil className="h-3 w-3" strokeWidth={2.4} />
                          Edit
                        </button>
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
  count,
}: {
  children: React.ReactNode;
  active: boolean;
  onClick: () => void;
  color: string;
  count?: number;
}) {
  // Dim chips with zero matches so the populated kinds stay visually dominant
  // (e.g. Waypoint 86 reads loud while the empty Charging/Pickup/… rows recede).
  const empty = count === 0 && !active;
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full pl-2.5 pr-2 h-7 text-[11px] font-medium tracking-tight transition-colors cursor-pointer",
        active
          ? "bg-[var(--color-ink-900)] text-white dark:bg-white/[0.12]"
          : empty
            ? "bg-white/35 dark:bg-white/[0.02] text-[var(--color-ink-400)] hover:bg-white/55"
            : "bg-white/55 dark:bg-white/[0.04] text-[var(--color-ink-700)] hover:bg-white/80",
      )}
    >
      <span
        className={cn(
          "h-1.5 w-1.5 rounded-full transition-opacity",
          empty && "opacity-40",
        )}
        style={{ background: color }}
      />
      <span>{children}</span>
      {count !== undefined && (
        <span
          className={cn(
            "font-mono tabular-nums text-[10px] tracking-tight rounded-full px-1.5 py-px",
            active
              ? "bg-white/15 text-white/80"
              : empty
                ? "bg-transparent text-[var(--color-ink-300)]"
                : "bg-white/55 dark:bg-white/[0.06] text-[var(--color-ink-600)]",
          )}
        >
          {count}
        </span>
      )}
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
