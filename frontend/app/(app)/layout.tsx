import { cookies } from "next/headers";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";
import { RAIL_COLLAPSED_LANE, RAIL_COOKIE, RAIL_EXPANDED_LANE } from "@/lib/shell";

// The signed-in app shell. Every page under app/(app) shares it, so
// navigating between them keeps the top bar and rail mounted — they used
// to live inside each page and replayed their ~1s entrance on every
// click. The route group adds nothing to the URL.
//
// The rail's expanded state comes from a cookie so the server renders it
// at the right width; with localStorage it rendered collapsed and then
// sprang open after hydration.
export default async function AppShellLayout({ children }: { children: React.ReactNode }) {
  const railExpanded = (await cookies()).get(RAIL_COOKIE)?.value === "1";
  return (
    <>
      {/* Seeds --rail-width for the first paint; LeftRail takes over on
          <html> once mounted (and outranks this rule while it does). */}
      <style>{`:root{--rail-width:${railExpanded ? RAIL_EXPANDED_LANE : RAIL_COLLAPSED_LANE}}`}</style>
      <TopNav />
      <LeftRail initialExpanded={railExpanded} />
      {children}
    </>
  );
}
