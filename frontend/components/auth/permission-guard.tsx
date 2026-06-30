"use client";

import { useAuth } from "@/components/auth/auth-provider";

/**
 * Phase S.6 — page-level gate that renders `children` only when the
 * current principal holds `requires` (or a wildcard that covers it).
 *
 * - While permissions are still loading (`null`), shows a thin "Loading
 *   permissions…" line so the layout doesn't jump when the gate decides.
 * - When the principal lacks the permission, renders a small panel
 *   naming the required code so ops know what to grant. The backend is
 *   still the authoritative enforcer — this is a UX courtesy gate.
 *
 * Use at the top of each admin page after `"use client"`.
 */
export function PermissionGuard({
  requires,
  children,
}: {
  requires: string;
  children: React.ReactNode;
}) {
  const { permissions, hasPermission } = useAuth();

  if (permissions === null) {
    return (
      <div className="px-6 py-10 text-sm text-[var(--color-ink-400)]">
        Loading permissions…
      </div>
    );
  }

  if (!hasPermission(requires)) {
    return (
      <div className="mx-auto max-w-md px-6 py-12">
        <div className="rounded-lg border border-[var(--color-ink-200)] bg-[var(--color-surface-1)] px-5 py-6 text-center">
          <h2 className="text-sm font-semibold text-[var(--color-ink-700)]">
            Insufficient permissions
          </h2>
          <p className="mt-2 text-xs text-[var(--color-ink-500)]">
            This page requires the permission
          </p>
          <code className="mt-3 inline-block rounded bg-[var(--color-surface-2)] px-2 py-1 font-mono text-[11px] text-[var(--color-ink-600)]">
            {requires}
          </code>
          <p className="mt-3 text-[11px] text-[var(--color-ink-400)]">
            Contact your administrator to request access.
          </p>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
