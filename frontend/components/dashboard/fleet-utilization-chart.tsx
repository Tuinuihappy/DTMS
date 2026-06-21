"use client";

import { useMemo } from "react";
import { formatDateTime, formatTime } from "@/lib/datetime";
import {
  Area,
  AreaChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { FleetUtilizationBucket } from "@/lib/api/dashboard";

// Phase P3.2 — Stacked area chart of hourly fleet utilization. Y axis is
// vehicle count by state (busy/idle/charging/maintenance/offline).
// LowBattery is intentionally omitted from the stack — it's a sub-condition
// counted elsewhere on the page.

const SERIES: Array<{
  key: keyof FleetUtilizationBucket;
  label: string;
  color: string;
}> = [
  { key: "busy",         label: "Busy",         color: "#f59e0b" },
  { key: "idle",         label: "Idle",         color: "#94a3b8" },
  { key: "charging",     label: "Charging",     color: "#60a5fa" },
  { key: "maintenance",  label: "Maintenance",  color: "#ef4444" },
  { key: "offline",      label: "Offline",      color: "#64748b" },
];

export function FleetUtilizationChart({
  buckets,
}: {
  buckets: FleetUtilizationBucket[];
}) {
  const data = useMemo(
    () =>
      buckets.map((b) => ({
        ...b,
        ts: new Date(b.bucketHour).getTime(),
        label: formatTime(b.bucketHour),
      })),
    [buckets],
  );

  if (data.length === 0) {
    return (
      <div className="grid h-[320px] place-items-center text-[12.5px] text-[var(--color-ink-500)]">
        No utilization snapshots yet — the snapshot service runs every minute.
      </div>
    );
  }

  return (
    <div className="h-[340px]">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 10, right: 12, left: -10, bottom: 0 }}>
          <defs>
            {SERIES.map((s) => (
              <linearGradient key={s.key} id={`fugrad-${s.key}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={s.color} stopOpacity={0.7} />
                <stop offset="100%" stopColor={s.color} stopOpacity={0.08} />
              </linearGradient>
            ))}
          </defs>
          <CartesianGrid strokeDasharray="3 6" stroke="rgba(148,163,184,0.18)" vertical={false} />
          <XAxis
            dataKey="label"
            tick={{ fontSize: 11, fill: "var(--color-ink-500)" }}
            tickLine={false}
            axisLine={false}
            interval="preserveStartEnd"
          />
          <YAxis
            tick={{ fontSize: 11, fill: "var(--color-ink-500)" }}
            tickLine={false}
            axisLine={false}
            allowDecimals={false}
            width={32}
          />
          <Tooltip
            contentStyle={{
              borderRadius: 12,
              border: "1px solid var(--color-ink-100)",
              background: "var(--color-surface)",
              fontSize: 12,
              boxShadow: "0 12px 30px -10px rgba(15,23,42,0.25)",
            }}
            labelFormatter={(_, payload) => {
              const point = payload?.[0]?.payload as { bucketHour?: string } | undefined;
              return point?.bucketHour ? formatDateTime(point.bucketHour) : "";
            }}
          />
          <Legend
            wrapperStyle={{ fontSize: 11, paddingTop: 8 }}
            iconType="circle"
            iconSize={8}
          />
          {SERIES.map((s) => (
            <Area
              key={s.key}
              type="monotone"
              dataKey={s.key}
              stackId="fleet"
              name={s.label}
              stroke={s.color}
              strokeWidth={1.5}
              fill={`url(#fugrad-${s.key})`}
              isAnimationActive={false}
            />
          ))}
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
