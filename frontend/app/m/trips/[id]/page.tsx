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
      {/* The only way back in standalone mode (no browser back button), so
          it gets the 44px touch-target floor, not an inline text link. */}
      <div className="px-4 pt-2">
        <Link
          href="/m/trips"
          className="inline-flex min-h-11 items-center pr-4 text-sm text-zinc-400 hover:text-zinc-200"
        >
          ← Back to trips
        </Link>
      </div>
      <TripDetail tripId={id} />
    </main>
  );
}
