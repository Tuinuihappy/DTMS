import type { Metadata } from "next";
import { requireSession } from "@/lib/auth/server-session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { OperatorTabsNav } from "@/components/operator-pwa/operator-tabs-nav";
import { TripsList } from "@/components/operator-pwa/trips-list";

export const metadata: Metadata = { title: "Trips — DTMS Operator" };

export default async function OperatorTripsPage() {
  await requireSession("/m/login");
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <OperatorShellHeader title="My trips" />
      <OperatorTabsNav />
      <TripsList />
    </main>
  );
}
