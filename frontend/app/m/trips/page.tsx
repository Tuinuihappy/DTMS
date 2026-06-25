import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/auth/session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { TripsList } from "@/components/operator-pwa/trips-list";

export const metadata: Metadata = { title: "Trips — DTMS Operator" };

export default async function OperatorTripsPage() {
  const jar = await cookies();
  if (!jar.get(SESSION_COOKIE)?.value) {
    redirect("/m/login");
  }
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <OperatorShellHeader title="My trips" />
      <TripsList />
    </main>
  );
}
