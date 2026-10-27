"use client";

import { Check, MapPin, Pause, X, Zap } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useMemo, useRef } from "react";
import type { TripMissionDto } from "@/lib/api/trips";
import { sortMissionRows } from "@/lib/mission-order";
import { spring, T } from "@/lib/motion";
import { cn } from "@/lib/utils";
import { parseActCode, useActionNameIndex } from "@/lib/vendor/riot3-action-names";

// Compact step track for the trip drawer header: one dot per mission the
// robot has REPORTED, in the order it reported them, with a marker on the
// one in progress that slides along as the trip advances.
//
// Only reported missions — the trip API carries no planned mission list
// (it lives in the vendor request snapshot, returned only with
// includeRaw), so the track grows as the robot works and never shows
// "steps left". Decided with the user 2026-10-27; the full list stays in
// the Mission timeline below.
//
// Rows are (mission × state) events; a mission's step state is its LATEST
// row in the shared sort (lib/mission-order), and steps are ordered by
// each mission's first row.

type StepState = "done" | "current" | "failed" | "hung" | "cancelled";

type Step = {
  key: string;
  type: string;
  label: string;
  state: StepState;
  title: string;
};

function stepState(raw: string): StepState {
  switch (raw.toUpperCase()) {
    case "FINISHED":
      return "done";
    case "FAILED":
    case "REJECTED":
      return "failed";
    case "HANG":
      return "hung";
    case "CANCELED":
    case "CANCELLED":
      return "cancelled";
    default:
      return "current";
  }
}

export function MissionTrack({ missions }: { missions: TripMissionDto[] }) {
  const hasAct = missions.some((m) => parseActCode(m.actionName) !== null);
  const actionNames = useActionNameIndex(hasAct);

  const steps = useMemo<Step[]>(() => {
    const byKey = new Map<string, TripMissionDto>();
    const order: string[] = [];
    for (const m of sortMissionRows(missions)) {
      if (!byKey.has(m.missionKey)) order.push(m.missionKey);
      byKey.set(m.missionKey, m); // later rows win → latest state
    }
    return order.map((key, i) => {
      const m = byKey.get(key)!;
      const code = parseActCode(m.actionName);
      const label =
        m.stationName ?? (code ? actionNames.get(code) ?? m.actionName : m.actionName) ?? m.missionType;
      return {
        key,
        type: m.missionType.toUpperCase(),
        label: label ?? "",
        state: stepState(m.state),
        title: `#${i + 1} ${m.missionType} → ${label} · ${m.state}`,
      };
    });
  }, [missions, actionNames]);

  // Keep the newest / in-progress step in view on a long trip.
  const scrollerRef = useRef<HTMLOListElement>(null);
  const focusIndex = steps.findIndex((s) => s.state === "current" || s.state === "hung");
  const anchor = focusIndex >= 0 ? focusIndex : steps.length - 1;
  useEffect(() => {
    const el = scrollerRef.current?.children[anchor] as HTMLElement | undefined;
    el?.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
  }, [anchor, steps.length]);

  if (steps.length === 0) return null;

  return (
    <div className="mt-3">
      <div className="mb-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        Missions · {steps.length} reported
      </div>
      <ol
        ref={scrollerRef}
        aria-label="Mission progress"
        // pt-1: the scroller clips both axes; the "current" ring sits 4px
        // outside its dot.
        className="flex items-start overflow-x-auto pb-1 pt-1 [scrollbar-width:thin]"
      >
        <AnimatePresence initial={false}>
          {steps.map((s, i) => (
            <motion.li
              key={s.key}
              layout="position"
              initial={{ opacity: 0, scale: 0.6 }}
              animate={{ opacity: 1, scale: 1, transition: spring.snappy }}
              className="relative flex w-[64px] shrink-0 flex-col items-center gap-1"
              title={s.title}
            >
              {i > 0 && <Connector filled={steps[i - 1].state === "done"} />}
                <Dot step={s} />
                <span
                  className={cn(
                    "max-w-full truncate text-center font-mono text-[10px] leading-tight",
                    s.state === "current" ? "font-semibold text-[var(--color-ink-900)]" : "text-[var(--color-ink-500)]",
                  )}
                >
                  {s.label}
                </span>
            </motion.li>
          ))}
        </AnimatePresence>
      </ol>
    </div>
  );
}

// Cells are 64px with a 24px dot centred: the gap between two dots runs
// from x = -20 to x = 20 of this cell.
function Connector({ filled }: { filled: boolean }) {
  return (
    <span aria-hidden className="absolute -left-[20px] top-[11px] h-[2px] w-[40px] overflow-hidden rounded-full bg-[var(--color-ink-100)] dark:bg-white/10">
      <motion.span
        initial={false}
        animate={{ scaleX: filled ? 1 : 0 }}
        transition={T.base}
        className="absolute inset-0 origin-left bg-[var(--color-brand-500)]"
      />
    </span>
  );
}

function Dot({ step }: { step: Step }) {
  const TypeIcon = step.type === "MOVE" ? MapPin : Zap;
  const icon =
    step.state === "done" ? (
      <Check className="h-3 w-3" strokeWidth={3} />
    ) : step.state === "failed" || step.state === "cancelled" ? (
      <X className="h-3 w-3" strokeWidth={3} />
    ) : step.state === "hung" ? (
      <Pause className="h-3 w-3" strokeWidth={3} />
    ) : (
      <TypeIcon className="h-3 w-3" strokeWidth={2.6} />
    );

  return (
    <span className="relative grid h-6 w-6 place-items-center">
      {/* The "you are here" ring — one per drawer, slides between steps. */}
      {(step.state === "current" || step.state === "hung") && (
        <motion.span
          layoutId="mission-track-current"
          transition={spring.snappy}
          aria-hidden
          className={cn(
            "absolute -inset-1 rounded-full ring-2",
            step.state === "hung" ? "ring-[var(--color-amber)]/60" : "ring-[var(--color-brand-500)]/50",
          )}
        />
      )}
      <span
        className={cn(
          "relative grid h-6 w-6 place-items-center rounded-full transition-colors duration-200",
          step.state === "done" && "bg-[var(--color-brand-500)] text-white",
          step.state === "current" && "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]",
          step.state === "hung" && "bg-[var(--color-amber)] text-white",
          step.state === "failed" && "bg-[var(--color-coral)] text-white",
          step.state === "cancelled" && "bg-[var(--color-ink-200)] text-[var(--color-ink-600)] dark:bg-white/10",
        )}
      >
        {icon}
      </span>
    </span>
  );
}
