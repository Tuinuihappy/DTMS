"use client";

import { usePathname, useRouter } from "next/navigation";
import { useOperatorAlerts } from "./operator-alerts";

// Banners pinned to the top of every /m page. Fixed and overlaying the
// header on purpose: pushing the page down would move "Record pickup" /
// "Complete trip" out from under a thumb that is about to tap them.
// z-50 keeps dialogs (layer stack, z 100+) and toasts (z-200) above it.
export function AlertBanner() {
  const alerts = useOperatorAlerts();
  const pathname = usePathname();
  const router = useRouter();
  if (!alerts || pathname?.startsWith("/m/login")) return null;

  // On the pool page the new cards are already in view.
  const visible = alerts.alerts.filter((a) => !(a.kind === "pool-new" && pathname === "/m/pool"));
  if (visible.length === 0) return null;

  return (
    <div className="pointer-events-none fixed inset-x-0 top-0 z-50 mx-auto flex max-w-2xl flex-col gap-2 p-2">
      {visible.map((a) => {
        const danger = a.kind === "trip-ended";
        return (
          <div
            key={a.key}
            role={danger ? "alert" : "status"}
            className={
              "pointer-events-auto flex items-stretch overflow-hidden rounded-2xl border shadow-lg backdrop-blur " +
              (danger
                ? "border-red-700/70 bg-red-950/95 text-red-50"
                : "border-cyan-700/70 bg-zinc-900/95 text-zinc-50")
            }
          >
            <button
              type="button"
              onClick={() => {
                alerts.dismiss(a.key);
                router.push(a.href);
              }}
              className="flex min-w-0 flex-1 flex-col items-start gap-0.5 px-4 py-3 text-left"
            >
              <span className="text-sm font-semibold">{a.title}</span>
              {a.detail && (
                <span className={"truncate text-xs " + (danger ? "text-red-200" : "text-zinc-300")}>
                  {a.detail}
                </span>
              )}
              {alerts.audioLocked && (
                <span className="text-xs text-amber-300">แตะหน้าจอ 1 ครั้งเพื่อเปิดเสียงเตือน</span>
              )}
            </button>
            <button
              type="button"
              aria-label="ปิดการแจ้งเตือน"
              onClick={() => alerts.dismiss(a.key)}
              className={"px-4 text-lg " + (danger ? "text-red-200" : "text-zinc-400")}
            >
              ✕
            </button>
          </div>
        );
      })}
    </div>
  );
}
