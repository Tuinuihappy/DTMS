"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

// WMS PR-4b (PR-D) — Two-tab operator nav between "Pool" (available
// trips) and "My trips" (claimed). Rendered directly below the shell
// header on both pages so the operator can flip contexts in one tap.
// Highlight-only on the active tab; both routes are 1-hop from the home
// screen (no drill-down needed).
const tabs = [
  { href: "/m/pool", label: "Pool" },
  { href: "/m/trips", label: "My trips" },
] as const;

export function OperatorTabsNav() {
  const pathname = usePathname();
  return (
    <nav className="flex border-b border-zinc-900 text-sm">
      {tabs.map((t) => {
        const active = pathname === t.href || pathname?.startsWith(t.href + "/");
        return (
          <Link
            key={t.href}
            href={t.href}
            className={
              "flex-1 px-4 py-3 text-center transition-colors " +
              (active
                ? "border-b-2 border-cyan-400 font-semibold text-cyan-300"
                : "border-b-2 border-transparent text-zinc-400 hover:text-zinc-200")
            }
          >
            {t.label}
          </Link>
        );
      })}
    </nav>
  );
}
