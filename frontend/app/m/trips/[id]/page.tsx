import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import Link from "next/link";
import { SESSION_COOKIE } from "@/lib/auth/session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { TripDetail } from "@/components/operator-pwa/trip-detail";

export const metadata: Metadata = { title: "Trip — DTMS Operator" };

export default async function OperatorTripDetailPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const jar = await cookies();
  if (!jar.get(SESSION_COOKIE)?.value) {
    redirect("/m/login");
  }
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
