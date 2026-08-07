import type { Metadata } from "next";
import Link from "next/link";
import { requireSession } from "@/lib/auth/server-session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { TripDetail } from "@/components/operator-pwa/trip-detail";

export const metadata: Metadata = { title: "Trip — DTMS Operator" };

export default async function OperatorTripDetailPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  await requireSession("/m/login");
  const { id } = await params;
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <OperatorShellHeader title="Trip detail" />
      <div className="px-4 py-2 text-xs">
        <Link href="/m/trips" className="text-zinc-400 hover:text-zinc-200">
          ← Back to trips
        </Link>
      </div>
      <TripDetail tripId={id} />
    </main>
  );
}
