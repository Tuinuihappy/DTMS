import type { Metadata } from "next";
import { ManualOperatorBoard } from "@/components/dispatch/manual/manual-operator-board";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

export const metadata: Metadata = {
  title: "Manual operators — Dispatch",
  description: "Manual transport mode operator board, override approvals, and trip reassignment.",
};

// Same shell as /dispatch/trips — the board used to render bare, with no
// way in from the app and no way out once there.
export default function DispatchManualPage() {
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <ManualOperatorBoard />
      </main>
    </>
  );
}
