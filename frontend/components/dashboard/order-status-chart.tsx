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
import type { OrderFunnelBucket } from "@/lib/api/dashboard";

// Phase P3.2 — Stacked area chart of hourly Order status counts. The X
// axis is the hour bucket (rendered as the local time portion); the Y
// stack is the per-status counter columns. Used on /dashboard/orders.

const SERIES: Array<{
  key: keyof OrderFunnelBucket;
  label: string;
  color: string;
}> = [
  { key: "confirmed",          label: "Confirmed",   color: "#94a3b8" }, // ink
  { key: "dispatched",         label: "Dispatched",  color: "#f59e0b" }, // amber
  { key: "inProgress",         label: "In progress", color: "#fbbf24" }, // amber-light
  { key: "completed",          label: "Completed",   color: "#10b981" }, // success
  { key: "partiallyCompleted", label: "Partial",     color: "#34d399" }, // success-light
  { key: "failed",             label: "Failed",      color: "#ef4444" }, // coral
  { key: "cancelled",          label: "Cancelled",   color: "#f87171" }, // coral-light
  { key: "held",               label: "Held",        color: "#a78bfa" }, // lavender
];

export function OrderStatusChart({
  buckets,
}: {
  buckets: OrderFunnelBucket[];
}) {
  // Recharts wants epoch ms on the X axis for time scaling. We also
  // attach a `label` field so the tooltip can show local time without
  // re-formatting in the renderer.
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
        No bucket data in this window.
      </div>
    );
  }

  return (
    <div className="h-[340px]">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 10, right: 12, left: -10, bottom: 0 }}>
          <defs>
            {SERIES.map((s) => (
              <linearGradient key={s.key} id={`grad-${s.key}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={s.color} stopOpacity={0.6} />
                <stop offset="100%" stopColor={s.color} stopOpacity={0.05} />
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
              stackId="orders"
              name={s.label}
              stroke={s.color}
              strokeWidth={1.5}
              fill={`url(#grad-${s.key})`}
              isAnimationActive={false}
            />
          ))}
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
