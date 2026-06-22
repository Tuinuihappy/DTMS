"use client";

import { useEffect, useRef, useState } from "react";
import {
  motion,
  useMotionValue,
  useSpring,
} from "motion/react";
import {
  AlertTriangle,
  Battery,
  BatteryCharging,
  Pause,
  WifiOff,
  Zap,
} from "lucide-react";

import { cn } from "@/lib/utils";
import {
  getMapRobotPositions,
  type RobotPositionDto,
} from "@/lib/api/facility";

const POLL_MS = 1000;

/* -------------------------------------------------------------------------- */
/* useRobotPositions — polls /maps/{id}/robot-positions on a 1 s interval.    */
/* Skips when the tab is hidden (visibilityState !== "visible") so a parked   */
/* tab doesn't burn API + battery. Cancels any in-flight fetch on tick or    */
/* unmount via AbortController so we never race a stale response into state.  */
/* -------------------------------------------------------------------------- */
export function useRobotPositions(mapId: string | null): {
  positions: RobotPositionDto[];
  lastTickMs: number | null;
  error: string | null;
} {
  const [positions, setPositions] = useState<RobotPositionDto[]>([]);
  const [lastTickMs, setLastTickMs] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Stash the latest abort controller so a new tick can cancel the in-flight one.
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!mapId) {
      setPositions([]);
      setLastTickMs(null);
      return;
    }

    let cancelled = false;

    async function tick() {
      if (cancelled) return;
      if (typeof document !== "undefined" && document.visibilityState !== "visible") {
        return;
      }
      abortRef.current?.abort();
      const ctrl = new AbortController();
      abortRef.current = ctrl;
      try {
        const data = await getMapRobotPositions(mapId!, ctrl.signal);
        if (cancelled) return;
        setPositions(data);
        setLastTickMs(performance.now());
        setError(null);
      } catch (err) {
        if (cancelled) return;
        if ((err as { name?: string })?.name === "AbortError") return;
        setError(err instanceof Error ? err.message : "Robot stream failed");
      }
    }

    void tick();
    const id = window.setInterval(() => void tick(), POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") void tick();
    };
    document.addEventListener("visibilitychange", onVisible);

    return () => {
      cancelled = true;
      window.clearInterval(id);
      document.removeEventListener("visibilitychange", onVisible);
      abortRef.current?.abort();
    };
  }, [mapId]);

  return { positions, lastTickMs, error };
}

/* -------------------------------------------------------------------------- */
/* State palette — drives both the robot icon color and the hover tooltip.    */
/* -------------------------------------------------------------------------- */
type StateKey =
  | "MOVING"
  | "IDLE"
  | "CHARGING"
  | "ERROR"
  | "OFFLINE";

function classifyState(p: RobotPositionDto): StateKey {
  if (p.connectionState === "OFFLINE" || p.connectionState === "CONNECTIONBROKEN") return "OFFLINE";
  if (p.emergency || p.systemState === "ERROR") return "ERROR";
  if (p.charging || p.systemState === "CHARGING") return "CHARGING";
  if (p.systemState === "EXECUTING" || p.systemState === "MOVING") return "MOVING";
  return "IDLE";
}

const STATE_META: Record<
  StateKey,
  { color: string; label: string }
> = {
  MOVING: { color: "var(--color-live)", label: "Moving" },
  IDLE: { color: "var(--color-info)", label: "Idle" },
  CHARGING: { color: "var(--color-amber)", label: "Charging" },
  ERROR: { color: "var(--color-coral)", label: "Error" },
  OFFLINE: { color: "var(--color-ink-300)", label: "Offline" },
};

// Lucide Navigation icon polygon, transformed for our world-unit canvas:
// centered at (0,0), tip pointing +X axis so the heading-rotation wrapper
// behaves identically to the previous custom triangle. Source polygon
// (24×24 viewBox): 3,11 22,2 13,21 11,13 — see lucide.dev/icons/navigation.
function navigationArrowPoints(iconSize: number): string {
  const s = iconSize / 24;
  const c = Math.SQRT1_2;
  return (
    [
      [3, 11],
      [22, 2],
      [13, 21],
      [11, 13],
    ] as const
  )
    .map(([x, y]) => {
      const cx = (x - 12) * s;
      const cy = (y - 12) * s;
      return [c * (cx - cy), c * (cx + cy)] as const;
    })
    .map(([x, y]) => `${x.toFixed(3)},${y.toFixed(3)}`)
    .join(" ");
}

/* -------------------------------------------------------------------------- */
/* RobotLayer — rendered inside the CanvasCard's <svg>.                       */
/* Each robot is a <motion.g> animated via spring so the 1 s position polls    */
/* turn into fluid 60 fps motion. Layer is screen-space-aware via worldPerPx   */
/* so triangle/battery-ring stay readable at any zoom.                        */
/* -------------------------------------------------------------------------- */
export function RobotLayer({
  positions,
  worldPerPx,
  hoverId,
  onHover,
}: {
  positions: RobotPositionDto[];
  worldPerPx: number;
  hoverId: string | null;
  onHover: (deviceKey: string | null) => void;
}) {
  // Icon ~14 px tall on screen, battery ring at 11 px radius.
  const iconSize = 14 * worldPerPx;
  const ringR = 11 * worldPerPx;
  const stroke = 1.4 * worldPerPx;

  return (
    <g>
      {positions.map((p) => (
        <RobotMarker
          key={p.deviceKey}
          p={p}
          ringR={ringR}
          stroke={stroke}
          iconSize={iconSize}
          isHover={hoverId === p.deviceKey}
          onHover={onHover}
        />
      ))}
    </g>
  );
}

/* -------------------------------------------------------------------------- */
/* RobotMarker — per-robot subcomponent so we can use hooks (useMotionValue +  */
/* useSpring + useTransform) without violating the rules-of-hooks in the map.  */
/*                                                                             */
/* Why this isn't just `motion.g animate={{ x, y }}`: at the world-unit scales */
/* this map operates in (10⁴–10⁵), Motion's shorthand maps `x`/`y` to CSS      */
/* transform but the spring + SVG-coord interaction failed to commit the       */
/* translate. Binding a MotionValue → SVG `transform` attribute via useTransform */
/* avoids that path entirely and updates the attribute every frame from RAF.   */
/* -------------------------------------------------------------------------- */
function RobotMarker({
  p,
  ringR,
  stroke,
  iconSize,
  isHover,
  onHover,
}: {
  p: RobotPositionDto;
  ringR: number;
  stroke: number;
  iconSize: number;
  isHover: boolean;
  onHover: (deviceKey: string | null) => void;
}) {
  const state = classifyState(p);
  const meta = STATE_META[state];
  const isOffline = state === "OFFLINE";
  const opacity = isOffline ? 0.45 : 1;

  // Battery arc — empty = no arc, full = full circle.
  const battPct = Math.max(0, Math.min(100, p.batteryPercentage)) / 100;
  const battCircumference = 2 * Math.PI * ringR;
  const dashEmpty = battCircumference * (1 - battPct);
  const battColor = p.batteryPercentage < 20 ? "var(--color-coral)" : meta.color;

  // Spring-smoothed position. MotionValue seeded with the first poll's value so
  // there's no "fly in from origin" on mount. Subsequent polls .set() new targets;
  // the spring interpolates RAF-by-RAF for fluid motion between 1 s ticks.
  const xMv = useMotionValue(p.x);
  const yMv = useMotionValue(p.y);
  const thetaMv = useMotionValue(p.theta);
  const xSpring = useSpring(xMv, { stiffness: 90, damping: 22 });
  const ySpring = useSpring(yMv, { stiffness: 90, damping: 22 });
  const thetaSpring = useSpring(thetaMv, { stiffness: 110, damping: 24 });

  useEffect(() => {
    xMv.set(p.x);
    yMv.set(p.y);
    thetaMv.set(p.theta);
  }, [p.x, p.y, p.theta, xMv, yMv, thetaMv]);

  // Bypass Motion's component-layer attribute binding (which silently no-ops
  // for some SVG transform shapes at huge world units) and write the SVG
  // `transform` attribute by hand. Each spring's `.on("change")` fires on
  // every animation frame; setAttribute is cheap and guaranteed to commit.
  const groupRef = useRef<SVGGElement>(null);
  const triangleRef = useRef<SVGGElement>(null);
  useEffect(() => {
    const writeGroup = () => {
      const el = groupRef.current;
      if (!el) return;
      el.setAttribute("transform", `translate(${xSpring.get()} ${ySpring.get()})`);
    };
    const writeTriangle = () => {
      const el = triangleRef.current;
      if (!el) return;
      const deg = (thetaSpring.get() * 180) / Math.PI;
      el.setAttribute("transform", `rotate(${deg})`);
    };
    writeGroup();
    writeTriangle();
    const unsubs = [
      xSpring.on("change", writeGroup),
      ySpring.on("change", writeGroup),
      thetaSpring.on("change", writeTriangle),
    ];
    return () => unsubs.forEach((u) => u());
  }, [xSpring, ySpring, thetaSpring]);

  return (
    <motion.g
      ref={groupRef}
      animate={{ opacity }}
      transition={{ opacity: { duration: 0.3 } }}
      onMouseEnter={() => onHover(p.deviceKey)}
      onMouseLeave={() => onHover(null)}
      className="cursor-pointer"
    >
      {/* Active glow halo */}
      {!isOffline && (
        <motion.circle
          cx={0}
          cy={0}
          r={ringR * 1.55}
          fill={meta.color}
          opacity={0.18}
          animate={{ scale: [1, 1.15, 1] }}
          transition={{ duration: 2.4, repeat: Infinity, ease: "easeInOut" }}
        />
      )}

      {/* Battery ring backdrop */}
      <circle
        cx={0}
        cy={0}
        r={ringR}
        fill="none"
        stroke="var(--color-ink-200)"
        strokeOpacity={0.4}
        strokeWidth={stroke * 0.7}
      />
      <circle
        cx={0}
        cy={0}
        r={ringR}
        fill="none"
        stroke={battColor}
        strokeWidth={stroke * 1.2}
        strokeLinecap="round"
        strokeDasharray={`${battCircumference - dashEmpty} ${dashEmpty}`}
        transform="rotate(-90)"
      />

      {/* Navigation arrow (Lucide). Polygon points are pre-baked so the tip
          faces +X at rest; the triangleRef wrapper then rotates by RIOT3
          theta (radians → degrees) to match the robot's actual heading. */}
      <g ref={triangleRef}>
        <polygon
          points={navigationArrowPoints(iconSize)}
          fill={meta.color}
          stroke="white"
          strokeWidth={stroke * 0.6}
          strokeLinejoin="round"
          strokeLinecap="round"
        />
      </g>

      {/* Connection-broken dashed ring */}
      {isOffline && (
        <circle
          cx={0}
          cy={0}
          r={ringR * 1.3}
          fill="none"
          stroke="var(--color-ink-400)"
          strokeWidth={stroke * 0.8}
          strokeDasharray={`${ringR * 0.4} ${ringR * 0.3}`}
        />
      )}

      {/* Selected highlight ring */}
      {isHover && (
        <circle
          cx={0}
          cy={0}
          r={ringR * 1.45}
          fill="none"
          stroke={meta.color}
          strokeWidth={stroke * 1.3}
          strokeOpacity={0.9}
        />
      )}

      {/* Generous transparent hit area so hover is forgiving at low zoom. */}
      <circle cx={0} cy={0} r={ringR * 1.6} fill="transparent" />
    </motion.g>
  );
}

/* -------------------------------------------------------------------------- */
/* RobotTooltip — HTML overlay tooltip pinned to the hovered robot.           */
/* Rendered outside the SVG so text is real CSS (anti-aliased) and we get     */
/* glass styling. The parent computes screen-px coords from world coords.     */
/* -------------------------------------------------------------------------- */
export function RobotTooltip({
  robot,
  left,
  top,
}: {
  robot: RobotPositionDto;
  left: number;
  top: number;
}) {
  const state = classifyState(robot);
  const meta = STATE_META[state];
  return (
    <motion.div
      initial={{ opacity: 0, y: 6, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
      style={{ left, top }}
      className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-[calc(100%+12px)] glass-strong rounded-[16px] px-3.5 py-3 min-w-[220px] max-w-[280px] shadow-[0_30px_60px_-20px_rgba(15,23,42,0.45)]"
    >
      <div className="flex items-start gap-2.5">
        <span
          className="grid h-7 w-7 place-items-center rounded-[9px] text-white shrink-0"
          style={{ background: meta.color }}
        >
          {state === "CHARGING" ? (
            <BatteryCharging className="h-3.5 w-3.5" strokeWidth={2.4} />
          ) : state === "ERROR" ? (
            <AlertTriangle className="h-3.5 w-3.5" strokeWidth={2.4} />
          ) : state === "OFFLINE" ? (
            <WifiOff className="h-3.5 w-3.5" strokeWidth={2.4} />
          ) : (
            <Zap className="h-3.5 w-3.5" strokeWidth={2.4} />
          )}
        </span>
        <div className="min-w-0">
          <div className="font-display text-[13px] font-semibold tracking-tight text-[var(--color-ink-900)] truncate">
            {robot.deviceName || robot.deviceKey}
          </div>
          <div className="text-[10.5px] font-mono tracking-tight text-[var(--color-ink-500)] truncate">
            {meta.label} · {robot.systemState}
          </div>
        </div>
      </div>
      <div className="mt-2.5 grid grid-cols-3 gap-1.5">
        <Cell
          label="batt"
          value={`${robot.batteryPercentage}%`}
          tone={robot.batteryPercentage < 20 ? "warn" : "ok"}
          icon={<Battery className="h-2.5 w-2.5" strokeWidth={2.4} />}
        />
        <Cell label="x" value={Math.round(robot.x).toLocaleString("en-US")} mono />
        <Cell label="y" value={Math.round(robot.y).toLocaleString("en-US")} mono />
      </div>
      {(robot.orderKey || robot.orderName) && (
        <div className="mt-2.5 rounded-[10px] bg-white/55 dark:bg-white/[0.04] px-2.5 py-1.5">
          <div className="text-[9.5px] uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
            Current order
          </div>
          <div className="mt-0.5 font-mono text-[10.5px] tabular-nums tracking-tight text-[var(--color-ink-800)] truncate">
            {robot.orderName ?? robot.orderKey}
          </div>
          {robot.startToEnd && (
            <div className="mt-0.5 text-[10.5px] text-[var(--color-ink-500)] truncate">
              {robot.startToEnd}
            </div>
          )}
        </div>
      )}
      <div className="mt-2 flex items-center gap-1.5 text-[10px] font-mono tracking-tight text-[var(--color-ink-500)]">
        {robot.paused && (
          <span className="inline-flex items-center gap-0.5">
            <Pause className="h-2.5 w-2.5" strokeWidth={2.4} />
            paused
          </span>
        )}
        <span>{robot.connectionState.toLowerCase()}</span>
        <span className="opacity-50">·</span>
        <span>θ {robot.theta.toFixed(2)}</span>
      </div>
    </motion.div>
  );
}

function Cell({
  label,
  value,
  tone,
  mono,
  icon,
}: {
  label: string;
  value: string;
  tone?: "ok" | "warn";
  mono?: boolean;
  icon?: React.ReactNode;
}) {
  return (
    <div
      className={cn(
        "rounded-[8px] px-1.5 py-1 text-center",
        tone === "warn"
          ? "bg-[var(--color-amber-soft)] text-[#8a4a07] dark:text-[var(--color-amber)]"
          : "bg-white/55 dark:bg-white/[0.04] text-[var(--color-ink-800)]",
      )}
    >
      <div className="flex items-center justify-center gap-0.5 text-[8.5px] uppercase tracking-[0.12em] opacity-70">
        {icon}
        {label}
      </div>
      <div
        className={cn(
          "mt-0.5 text-[11px] font-semibold",
          mono && "font-mono tabular-nums tracking-tight",
        )}
      >
        {value}
      </div>
    </div>
  );
}
