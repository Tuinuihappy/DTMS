import type { Metadata } from "next";
import { requireSession } from "@/lib/auth/server-session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { OperatorTabsNav } from "@/components/operator-pwa/operator-tabs-nav";
import { PoolTripsList } from "@/components/operator-pwa/pool-trips-list";

export const metadata: Metadata = { title: "Pool — DTMS Operator" };

// WMS PR-4b (PR-D) — Operator pool view. Universal visibility — any
// active operator sees every dispatched, unclaimed Manual/Fleet trip and
// can tap to claim + start atomically. Realtime updates arrive via the
// /hubs/operator-pool SignalR hub (see PoolTripsList).
export default async function OperatorPoolPage() {
  await requireSession("/m/login");
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <OperatorShellHeader title="Trip pool" />
      <OperatorTabsNav />
      <PoolTripsList />
    </main>
  );
}
