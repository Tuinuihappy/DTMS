"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AnimatePresence, motion } from "motion/react";
import {
  BatteryCharging,
  Compass,
  Crosshair,
  Hexagon,
  Layers,
  MapPin,
  Maximize2,
  PackageOpen,
  ParkingCircle,
  Scan,
} from "lucide-react";

import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";
import {
  getStations,
  listMaps,
  type MapSummaryDto,
  type StationDto,
} from "@/lib/api/facility";
import {
  RobotLayer,
  RobotTooltip,
  useRobotPositions,
} from "@/components/facility/robot-layer";

/* -------------------------------------------------------------------------- */
/* Station-type lexicon — mirrors MapsExperience but drops the chip classes   */
/* the showcase doesn't need. The swatch is what the canvas reads.            */
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

const TYPE_SWATCH: Record<TypeKey, string> = {
  NORMAL: "var(--color-brand-500)",
  CHARGING: "var(--color-amber)",
  PICKUP: "#e07248",
  DROPOFF: "#16a37b",
  PARKING: "#7c6acd",
  DOCK: "#3b6bd6",
  CHECKPOINT: "var(--color-ink-500)",
};

const TYPE_ICON: Record<TypeKey, typeof MapPin> = {
  NORMAL: MapPin,
  CHARGING: BatteryCharging,
  PICKUP: PackageOpen,
  DROPOFF: Layers,
  PARKING: ParkingCircle,
  DOCK: Hexagon,
  CHECKPOINT: Crosshair,
};

function typeKey(raw: string | undefined | null): TypeKey {
  const t = (raw ?? "NORMAL").toString().toUpperCase();
  return (TYPE_KEYS as string[]).includes(t) ? (t as TypeKey) : "NORMAL";
}

/* -------------------------------------------------------------------------- */
/* HeroLiveMap — the showcase variant of the facility cartography.            */
/* Read-only by design: no edit drawer, no RIOT3 sync, no zoom controls       */
/* (wheel + pinch + double-click recenter still work — discoverable, not in   */
/* the way). Lives in a fixed-aspect frame so it sits beside the hero copy on */
/* desktop and stacks above on mobile without ever looking squashed.          */
/* -------------------------------------------------------------------------- */
export function HeroLiveMap() {
  const [maps, setMaps] = useState<MapSummaryDto[]>([]);
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [stations, setStations] = useState<StationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
        setError(err instanceof Error ? err.message : "Failed to load");
        setLoading(false);
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
        setError(null);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Failed to load");
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
    <motion.div
      initial={{ opacity: 0, y: 14 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.7, delay: 0.15, ease: [0.22, 1, 0.36, 1] }}
      className="relative"
    >
      {/* Aurora glow behind the frame — a soft halo so the card lifts off the
          page without needing a heavy drop-shadow. Hidden from a11y tree. */}
      <div
        aria-hidden
        className="absolute -inset-6 -z-10 rounded-[44px] opacity-70 blur-3xl"
        style={{
          background:
            "radial-gradient(60% 60% at 50% 40%, rgba(199, 204, 255, 0.55), transparent 70%)," +
            "radial-gradient(50% 50% at 80% 80%, rgba(216, 241, 228, 0.45), transparent 70%)",
        }}
      />

      <div className="relative overflow-hidden rounded-[var(--radius-xl)] glass-strong glass-edge">
        {/* Frame chrome — map identity, live indicator, full-screen link */}
        <div className="flex items-center justify-between gap-3 border-b border-white/40 px-4 py-3 dark:border-white/[0.06]">
          <div className="flex min-w-0 items-center gap-2.5">
            <span className="grid h-8 w-8 place-items-center rounded-[10px] bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_6px_14px_-6px_rgba(14,21,48,0.55)] dark:bg-[var(--color-brand-500)]">
              <Compass className="h-4 w-4" strokeWidth={2.2} />
            </span>
            <div className="min-w-0">
              <div className="font-display text-[13px] font-semibold tracking-tight text-[var(--color-ink-900)] truncate">
                {selectedMap?.name ?? "Facility cartography"}
              </div>
              <div className="font-mono text-[10px] tracking-tight text-[var(--color-ink-500)]">
                {selectedMap ? (
                  <span>
                    {selectedMap.activeStationCount}/{selectedMap.stationCount} stations
                    {selectedMap.vendorRef ? ` · RIOT ${selectedMap.vendorRef}` : ""}
                  </span>
                ) : (
                  <span>live topology</span>
                )}
              </div>
            </div>
          </div>

          <div className="flex shrink-0 items-center gap-2">
            <span className="hidden sm:inline-flex items-center gap-1.5 rounded-full bg-white/70 px-2.5 py-1 text-[10.5px] font-mono tracking-tight text-[var(--color-ink-600)] dark:bg-white/[0.06]">
              <Scan className="h-3 w-3" strokeWidth={2.4} />
              live
            </span>
          </div>
        </div>

        {/* Canvas frame — fixed aspect so it pairs cleanly beside hero copy on
            desktop. On mobile we relax the floor so it still occupies a useful
            slice of the viewport without dominating it. */}
        <div className="relative aspect-[5/6] sm:aspect-[4/5] md:aspect-[5/6] xl:aspect-[6/7]">
          <MapCanvas
            map={selectedMap}
            stations={stations}
            loading={loading}
            error={error}
          />
        </div>

        {/* Map selector — only shown when there's more than one facility map. */}
        {maps.length > 1 && (
          <div className="flex gap-1.5 overflow-x-auto border-t border-white/40 px-3 py-2 dark:border-white/[0.06]">
            {maps.map((m) => {
              const active = m.id === selectedMapId;
              return (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => setSelectedMapId(m.id)}
                  className={cn(
                    "shrink-0 rounded-full px-3 py-1 text-[10.5px] font-semibold tracking-tight transition-colors cursor-pointer",
                    active
                      ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
                      : "bg-white/60 text-[var(--color-ink-700)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.1]",
                  )}
                >
                  {m.name}
                </button>
              );
            })}
          </div>
        )}
      </div>
    </motion.div>
  );
}

/* -------------------------------------------------------------------------- */
/* MapCanvas — the actual SVG renderer. Owns the viewport, gestures, and the  */
/* robot overlay. Trimmed-down sibling of MapsExperience.CanvasCard.          */
/* -------------------------------------------------------------------------- */
function MapCanvas({
  map,
  stations,
  loading,
  error,
}: {
  map: MapSummaryDto | null;
  stations: StationDto[];
  loading: boolean;
  error: string | null;
}) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [svgWidth, setSvgWidth] = useState(600);
  const [hoverId, setHoverId] = useState<string | null>(null);
  const [pinned, setPinned] = useState<string | null>(null);
  const [hoverRobotId, setHoverRobotId] = useState<string | null>(null);

  const { positions: robots, lastTickMs } = useRobotPositions(map?.id ?? null);

  // Force re-render every 30 s so the "X s ago" indicator stays current
  // even when no robots arrive (e.g. RIOT3 reachable but empty).
  const [, forceTick] = useState(0);
  useEffect(() => {
    const t = window.setInterval(() => forceTick((n) => (n + 1) % 1_000), 30_000);
    return () => window.clearInterval(t);
  }, []);

  useEffect(() => {
    if (!svgRef.current) return;
    const el = svgRef.current;
    const update = () => {
      const w = el.getBoundingClientRect().width;
      if (w > 0) setSvgWidth(w);
    };
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // World bounds across stations + robots so a stray robot is never cropped.
  // Adds 8 % padding so points never touch the frame edge.
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

  // Viewport mirrors MapsExperience: only reset on map switch so a hover/pan
  // doesn't jump every poll. The showcase doesn't expose buttons; we keep the
  // wheel+pinch+double-click gestures for power users who notice them.
  const [view, setView] = useState({
    x: bounds.minX,
    y: bounds.minY,
    w: viewW,
    h: viewH,
  });
  const lastMapIdRef = useRef<string | null>(null);
  useEffect(() => {
    if (!map?.id) return;
    if (lastMapIdRef.current === map.id) return;
    lastMapIdRef.current = map.id;
    setView({ x: bounds.minX, y: bounds.minY, w: viewW, h: viewH });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map?.id]);

  // Pointer gestures — same model as MapsExperience but trimmed: tap closes
  // pinned card, drag pans, two-finger pinch zooms. No edit-station path.
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
  const CLICK_THRESHOLD = 6;

  const recenter = useCallback(() => {
    setView({ x: bounds.minX, y: bounds.minY, w: viewW, h: viewH });
  }, [bounds.minX, bounds.minY, viewW, viewH]);

  const beginGesture = useCallback((e: React.PointerEvent<SVGSVGElement>) => {
    if (e.pointerType === "mouse" && e.button !== 0) return;
    if (!svgRef.current) return;
    pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
    const v = viewRef.current;
    if (pointers.current.size === 2) {
      const pts = [...pointers.current.values()];
      const rect = svgRef.current.getBoundingClientRect();
      const dist = Math.hypot(pts[1]!.x - pts[0]!.x, pts[1]!.y - pts[0]!.y);
      pinchStart.current = {
        dist: Math.max(dist, 1),
        rectW: rect.width,
        rectH: rect.height,
        midPx: (pts[0]!.x + pts[1]!.x) / 2 - rect.left,
        midPy: (pts[0]!.y + pts[1]!.y) / 2 - rect.top,
        view: { x: v.x, y: v.y, w: v.w, h: v.h },
      };
      panStart.current = null;
      dragMoved.current = true;
    } else if (pointers.current.size === 1) {
      panStart.current = { cx: e.clientX, cy: e.clientY, vx: v.x, vy: v.y };
      dragMoved.current = false;
    }
    setIsGesturing(true);
  }, []);

  useEffect(() => {
    if (!isGesturing) return;
    const onMove = (e: PointerEvent) => {
      if (!pointers.current.has(e.pointerId)) return;
      pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
      const svg = svgRef.current;
      if (!svg) return;
      const rect = svg.getBoundingClientRect();
      if (pointers.current.size === 2 && pinchStart.current) {
        const pts = [...pointers.current.values()];
        const dist = Math.hypot(pts[1]!.x - pts[0]!.x, pts[1]!.y - pts[0]!.y);
        const midX = (pts[0]!.x + pts[1]!.x) / 2 - rect.left;
        const midY = (pts[0]!.y + pts[1]!.y) / 2 - rect.top;
        const ps = pinchStart.current;
        const scale = ps.dist / Math.max(dist, 1);
        const newW = ps.view.w * scale;
        const newH = ps.view.h * scale;
        const minSize = 1000;
        const maxSize = Math.max(viewW, viewH) * 4;
        if (newW < minSize || newH < minSize) return;
        if (newW > maxSize || newH > maxSize) return;
        const wx = ps.view.x + (ps.midPx / ps.rectW) * ps.view.w;
        const wy = ps.view.y + (1 - ps.midPy / ps.rectH) * ps.view.h;
        setView({
          x: wx - (midX / rect.width) * newW,
          y: wy - (1 - midY / rect.height) * newH,
          w: newW,
          h: newH,
        });
        return;
      }
      const ps = panStart.current;
      if (!ps) return;
      const dxPx = e.clientX - ps.cx;
      const dyPx = e.clientY - ps.cy;
      if (Math.abs(dxPx) + Math.abs(dyPx) > CLICK_THRESHOLD) dragMoved.current = true;
      setView((v) => ({
        ...v,
        x: ps.vx - (dxPx / rect.width) * v.w,
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

  // Wheel zoom — same algebra as MapsExperience, anchored at the cursor.
  useEffect(() => {
    const el = svgRef.current;
    if (!el) return;
    const handler = (e: WheelEvent) => {
      e.preventDefault();
      const rect = el.getBoundingClientRect();
      const px = e.clientX - rect.left;
      const py = e.clientY - rect.top;
      setView((v) => {
        const factor = e.deltaY > 0 ? 1.2 : 1 / 1.2;
        const newW = v.w * factor;
        const newH = v.h * factor;
        const minSize = 1000;
        const maxSize = Math.max(viewW, viewH) * 4;
        if (newW < minSize || newH < minSize) return v;
        if (newW > maxSize || newH > maxSize) return v;
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

  // Double-tap recenter for touch — same threshold-based pattern.
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

  const pxPerWorld = svgWidth > 0 ? svgWidth / view.w : 1;
  const worldPerPx = 1 / pxPerWorld;
  const DOT_PX = 4.2;
  const RING_PX = 9;
  const HALO_PX = 18;
  const STROKE_PX = 1;
  const r = DOT_PX * worldPerPx;
  const ringR = RING_PX * worldPerPx;
  const haloR = HALO_PX * worldPerPx;
  const sw = STROKE_PX * worldPerPx;
  const sw2 = 1.4 * worldPerPx;

  if (error && !loading) {
    return (
      <div className="absolute inset-0 grid place-items-center px-6 text-center">
        <div className="max-w-xs text-[12.5px] text-[var(--color-ink-500)]">
          Live map unavailable — {error}
        </div>
      </div>
    );
  }

  if (loading && stations.length === 0) {
    return <CanvasSkeleton />;
  }

  return (
    <>
      <svg
        ref={svgRef}
        role="img"
        aria-label="Live facility cartography with active stations and robots"
        viewBox={`${view.x} ${view.y} ${view.w} ${view.h}`}
        preserveAspectRatio="xMidYMid meet"
        className="absolute inset-0 h-full w-full select-none"
        style={{ cursor: isGesturing ? "grabbing" : "grab", touchAction: "none" }}
        onPointerDown={beginGesture}
        onPointerUp={onTapEnd}
        onDoubleClick={recenter}
        onPointerLeave={(e) => {
          if (e.pointerType === "touch") return;
          setHoverId(null);
        }}
      >
        <defs>
          {TYPE_KEYS.map((key) => (
            <radialGradient
              key={key}
              id={`hero-station-glow-${key}`}
              cx="50%"
              cy="50%"
              r="50%"
            >
              <stop offset="0%" stopColor={TYPE_SWATCH[key]} stopOpacity="0.7" />
              <stop offset="60%" stopColor={TYPE_SWATCH[key]} stopOpacity="0.12" />
              <stop offset="100%" stopColor={TYPE_SWATCH[key]} stopOpacity="0" />
            </radialGradient>
          ))}
          {/* Subtle dot grid pattern in world space — anchors the map */}
          <pattern
            id="hero-grid-dots"
            x="0"
            y="0"
            width={Math.max(viewW, viewH) / 30}
            height={Math.max(viewW, viewH) / 30}
            patternUnits="userSpaceOnUse"
          >
            <circle cx={0} cy={0} r={Math.max(viewW, viewH) / 1800} fill="currentColor" opacity={0.5} />
          </pattern>
        </defs>

        {/* Y-flip wrapper — RIOT3 / robotics convention uses Y-up; SVG defaults
            to Y-down. Same flip as MapsExperience so data coords align 1:1. */}
        <g transform={`translate(0 ${2 * view.y + view.h}) scale(1 -1)`}>
          {/* Grid backdrop — text-color drives the dot tint so it inverts in dark mode */}
          <rect
            x={view.x}
            y={view.y}
            width={view.w}
            height={view.h}
            fill="url(#hero-grid-dots)"
            className="text-[var(--color-ink-300)]"
          />

          {stations.map((s, i) => {
            const k = typeKey(s.type);
            const swatch = TYPE_SWATCH[k];
            const isActiveDot = s.id === active;
            return (
              <motion.g
                key={s.id}
                initial={{ opacity: 0, scale: 0.4 }}
                animate={{ opacity: 1, scale: 1 }}
                transition={{
                  duration: 0.45,
                  delay: 0.15 + Math.min(i, 40) * 0.012,
                  ease: [0.22, 1, 0.36, 1],
                }}
                style={{ transformOrigin: `${s.x}px ${s.y}px` }}
                onMouseEnter={() => setHoverId(s.id)}
                onMouseLeave={() => setHoverId((id) => (id === s.id ? null : id))}
                onClick={(e) => {
                  if (dragMoved.current) return;
                  e.stopPropagation();
                  setPinned((id) => (id === s.id ? null : s.id));
                }}
                className="cursor-pointer"
              >
                {s.isActive && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={haloR}
                    fill={`url(#hero-station-glow-${k})`}
                    opacity={0.5}
                  />
                )}
                {s.isActive && (
                  <motion.circle
                    cx={s.x}
                    cy={s.y}
                    r={ringR}
                    fill="none"
                    stroke={swatch}
                    strokeOpacity={0.32}
                    strokeWidth={sw2}
                    initial={{ opacity: 0.5, scale: 0.85 }}
                    animate={{ opacity: [0.5, 0, 0.5], scale: [0.85, 1.5, 0.85] }}
                    transition={{
                      duration: 2.6,
                      repeat: Infinity,
                      ease: "easeInOut",
                      delay: (i % 7) * 0.18,
                    }}
                    style={{ transformOrigin: `${s.x}px ${s.y}px` }}
                  />
                )}
                {isActiveDot && (
                  <circle
                    cx={s.x}
                    cy={s.y}
                    r={ringR * 1.15}
                    fill="none"
                    stroke={swatch}
                    strokeWidth={sw * 1.6}
                    strokeOpacity={0.9}
                  />
                )}
                <circle
                  cx={s.x}
                  cy={s.y}
                  r={r}
                  fill={swatch}
                  fillOpacity={s.isActive ? 1 : 0.3}
                  stroke="white"
                  strokeOpacity={0.9}
                  strokeWidth={sw2 * 0.55}
                />
              </motion.g>
            );
          })}

          <RobotLayer
            positions={robots}
            worldPerPx={worldPerPx}
            hoverId={hoverRobotId}
            onHover={setHoverRobotId}
          />
        </g>
      </svg>

      {/* Robot tooltip — same screen-space math as MapsExperience. */}
      {(() => {
        const hovered = robots.find((r) => r.deviceKey === hoverRobotId);
        if (!hovered) return null;
        const rect = svgRef.current?.getBoundingClientRect();
        if (!rect) return null;
        const px = ((hovered.x - view.x) / view.w) * rect.width;
        const py = ((view.y + view.h - hovered.y) / view.h) * rect.height;
        return <RobotTooltip robot={hovered} left={px} top={py} />;
      })()}

      {/* Top-left: live indicator chip */}
      <div className="pointer-events-none absolute left-3 top-3 flex items-center gap-1.5 rounded-full glass px-2.5 py-1 text-[10.5px] font-mono tracking-tight text-[var(--color-ink-700)]">
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

      {/* Bottom-right: recenter button — the only visible control. Keeps the
          showcase clean while still giving curious users a way out. */}
      <button
        type="button"
        aria-label="Recenter map"
        onClick={recenter}
        className="absolute bottom-3 right-3 grid h-8 w-8 place-items-center rounded-full glass-strong text-[var(--color-ink-700)] transition-transform hover:-translate-y-0.5 cursor-pointer"
      >
        <Maximize2 className="h-3.5 w-3.5" strokeWidth={2.2} />
      </button>

      {/* Bottom-left: hint */}
      <div className="pointer-events-none absolute bottom-3 left-3 hidden sm:flex items-center gap-1.5 rounded-full glass px-2.5 py-1 text-[10px] font-mono tracking-tight text-[var(--color-ink-500)]">
        drag · scroll · pinch
      </div>

      {/* Pinned station chip — tucked into the top-right so it never collides
          with the recenter button or the live indicator. */}
      <AnimatePresence>
        {activeStation && (
          <motion.div
            key={activeStation.id}
            initial={{ opacity: 0, y: 6, scale: 0.96 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 6, scale: 0.96 }}
            transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
            className="pointer-events-auto absolute right-3 top-3 max-w-[200px] glass-strong rounded-[14px] px-3 py-2"
          >
            <StationChip station={activeStation} />
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}

/* -------------------------------------------------------------------------- */
/* Small subcomponents                                                        */
/* -------------------------------------------------------------------------- */
function StationChip({ station }: { station: StationDto }) {
  const k = typeKey(station.type);
  const Icon = TYPE_ICON[k];
  const swatch = TYPE_SWATCH[k];
  return (
    <div className="flex items-center gap-2">
      <span
        className="grid h-7 w-7 place-items-center rounded-[8px] text-white shrink-0"
        style={{ background: swatch }}
      >
        <Icon className="h-3.5 w-3.5" strokeWidth={2.4} />
      </span>
      <div className="min-w-0">
        <div className="font-display text-[11.5px] font-semibold tracking-tight text-[var(--color-ink-900)] truncate">
          {station.name}
        </div>
        <div className="font-mono text-[9.5px] tracking-tight text-[var(--color-ink-500)]">
          {k.toLowerCase()}{station.code ? ` · ${station.code}` : ""}
        </div>
      </div>
    </div>
  );
}

function CanvasSkeleton() {
  return (
    <div className="absolute inset-0 grid place-items-center">
      <div className="flex flex-col items-center gap-3 text-[var(--color-ink-400)]">
        <motion.div
          animate={{ rotate: 360 }}
          transition={{ duration: 2.4, repeat: Infinity, ease: "linear" }}
          className="grid h-10 w-10 place-items-center rounded-full bg-white/70 dark:bg-white/[0.06]"
        >
          <Compass className="h-5 w-5" strokeWidth={2.2} />
        </motion.div>
        <span className="font-mono text-[10.5px] tracking-tight">
          loading topology
        </span>
      </div>
    </div>
  );
}
