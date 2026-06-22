// =============================================================================
// FRONTEND COMPONENT TEMPLATE
// =============================================================================
//
// Stack:
//   - Next.js 16 + React (per memory: project_frontend_layout)
//   - TypeScript strict
//   - shadcn/ui — base-ui variant (use `render` prop, NOT `asChild`)
//   - Tailwind CSS
//   - SWR for data fetching (existing convention)
//   - lucide-react for icons
//
// Folder layout:
//   frontend/components/transport/{mode}/{component-name}.tsx
//   - mode = amr | manual | fleet (per ADR-001 module split)
//   - kebab-case filenames
//   - Component names PascalCase, default export OR named export
//
// Reference examples (read these first):
//   frontend/components/dispatch/trip-action-bar.tsx
//     ↑ canonical: client component with state + API calls + mode-aware buttons
//   frontend/components/dispatch/trip-detail-drawer.tsx
//     ↑ Slide-in panel with SWR data + SignalR live updates
//   frontend/components/dispatch/mission-timeline.tsx
//     ↑ Pure presentation component
//   frontend/components/primitives/station-combobox.tsx
//     ↑ Reusable picker (to be split per Phase 2)
//   frontend/lib/api/trips.ts
//     ↑ API client pattern (fetch helpers + DTO types)
//
// Critical conventions:
//   1. Add "use client" ONLY when component uses hooks / event handlers / browser APIs
//   2. Use markdown link syntax in any user-facing copy that points to files
//   3. Tailwind classes via cn() helper (from @/lib/utils) for conditional classes
//   4. NEVER call backend from server components — use API routes / RSC fetch
//   5. Date display ALWAYS via <DateTime /> primitive (per recent commit fe44caa)
//   6. Hex colors are 60+ tokenized — use design tokens, NOT raw hex (per commit 46f5632)
//   7. Hooks at top, derived state next, handlers below, render at bottom
//   8. Props typed inline with object destructuring (smaller component pattern)
//      OR with separate Props interface (larger components > 100 lines)
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

"use client";   // ← REMOVE if pure presentation (no hooks, no handlers)

import { useState, useEffect } from "react";
import useSWR from "swr";
import { ChevronRight, Loader2 } from "lucide-react";   // pick icons you need

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { DateTime } from "@/components/primitives/date-time";   // standard date display

// API + types
import { type TripStatus, type TripDto } from "@/lib/api/trips";
import { acknowledgeTrip } from "@/lib/api/manual-operator";    // example

// =============================================================================
// PROPS (inline for small, separate interface for large)
// =============================================================================

// For larger components use separate interface:
//
// interface {ComponentName}Props {
//   tripId: string;
//   mode: "Amr" | "Manual" | "Fleet";
//   onAction?: (action: string) => void;
// }

// =============================================================================
// COMPONENT
// =============================================================================

export function {ComponentName}({
  tripId,
  mode,
  hasVendorIssue = false,
  onAction,
}: {
  tripId: string;
  mode: "Amr" | "Manual" | "Fleet";
  hasVendorIssue?: boolean;
  onAction?: (action: string, payload?: { newTripId?: string }) => void;
}) {
  // ─── State (top) ──────────────────────────────────────────────────────
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // ─── Data fetching ────────────────────────────────────────────────────
  const { data: trip, isLoading, mutate } = useSWR<TripDto>(
    `/api/trips/${tripId}`,
    fetcher,
    {
      refreshInterval: 5_000,   // optional polling
      revalidateOnFocus: true,
    },
  );

  // ─── Derived state ────────────────────────────────────────────────────
  const canAcknowledge = mode === "Manual" && trip?.status === "Created";
  const canPause = trip?.status === "InProgress";
  const canResume = trip?.status === "Paused";

  // ─── Effects ──────────────────────────────────────────────────────────
  useEffect(() => {
    // Subscribe to SignalR / WebSocket for live updates if needed
    // const unsubscribe = subscribeToTripUpdates(tripId, () => mutate());
    // return unsubscribe;
  }, [tripId, mutate]);

  // ─── Handlers ─────────────────────────────────────────────────────────
  const handleAction = async (
    action: string,
    fn: () => Promise<unknown>,
  ) => {
    setBusy(action);
    setError(null);
    try {
      await fn();
      await mutate();   // refetch
      onAction?.(action);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Action failed");
    } finally {
      setBusy(null);
    }
  };

  // ─── Loading / error states (early returns) ───────────────────────────
  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-6 text-muted-foreground">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        Loading trip...
      </div>
    );
  }

  if (!trip) {
    return (
      <Card className="border-destructive/40">
        <CardContent className="p-6 text-sm text-destructive">
          Trip {tripId} not found.
        </CardContent>
      </Card>
    );
  }

  // ─── Render ───────────────────────────────────────────────────────────
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <div>
          <CardTitle className="text-base">Trip {trip.id.slice(0, 8)}</CardTitle>
          <p className="text-xs text-muted-foreground">
            Created <DateTime value={trip.createdAt} />
          </p>
        </div>
        <Badge variant={statusVariant(trip.status)}>{trip.status}</Badge>
      </CardHeader>

      <CardContent className="space-y-3">
        {/* Mode-aware UI per ADR-001 — different controls per transport mode */}
        {mode === "Amr" && (
          <AmrSpecificSection trip={trip} />
        )}
        {mode === "Manual" && (
          <ManualSpecificSection trip={trip} />
        )}
        {mode === "Fleet" && (
          <FleetSpecificSection trip={trip} />
        )}

        {/* Common actions */}
        <div className="flex flex-wrap gap-2 border-t pt-3">
          {canAcknowledge && (
            <Button
              size="sm"
              disabled={busy !== null}
              onClick={() => handleAction("acknowledge", () => acknowledgeTrip(tripId))}
              className={cn(
                "bg-primary text-primary-foreground",
                hasVendorIssue && "ring-2 ring-warning/40",   // tokenized class
              )}
            >
              {busy === "acknowledge" ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <ChevronRight className="h-4 w-4" />
              )}
              Acknowledge
            </Button>
          )}
        </div>

        {error && (
          <p className="text-sm text-destructive" role="alert">
            {error}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

// =============================================================================
// SUB-COMPONENTS (co-located if small + only used here)
// =============================================================================

function AmrSpecificSection({ trip }: { trip: TripDto }) {
  return (
    <div className="text-sm">
      <p>Vendor: {trip.vendorVehicleName ?? "—"}</p>
      <p>Order key: {trip.vendorOrderKey ?? "(pending)"}</p>
    </div>
  );
}

function ManualSpecificSection({ trip }: { trip: TripDto }) {
  return (
    <div className="text-sm">
      <p>Operator: {trip.assignedOperatorName ?? "—"}</p>
      {trip.expectedPickupBy && (
        <p>
          Pickup by: <DateTime value={trip.expectedPickupBy} />
        </p>
      )}
    </div>
  );
}

function FleetSpecificSection({ trip }: { trip: TripDto }) {
  return (
    <div className="text-sm">
      <p>Waybill: {trip.waybillNumber ?? "—"}</p>
      {trip.trackingUrl && (
        <a
          href={trip.trackingUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="text-primary underline-offset-4 hover:underline"
        >
          Track shipment →
        </a>
      )}
    </div>
  );
}

// =============================================================================
// HELPERS (extract to module if reused)
// =============================================================================

function statusVariant(
  status: TripStatus,
): "default" | "secondary" | "destructive" | "success" {
  switch (status) {
    case "Completed":
      return "success";
    case "Failed":
    case "Cancelled":
      return "destructive";
    case "InProgress":
    case "Paused":
      return "default";
    default:
      return "secondary";
  }
}

async function fetcher(url: string) {
  const res = await fetch(url, { credentials: "include" });
  if (!res.ok) {
    throw new Error(`Request failed: ${res.status}`);
  }
  return res.json();
}


// =============================================================================
// COMPANION: API CLIENT (lib/api/manual-operator.ts)
// =============================================================================
//
// Per existing convention (lib/api/action-templates.ts, trips.ts):
//
// // Browser-side fetch helpers + DTO types for the Manual operator module.
// // Backend at /api/operator responds with the RIOT3-style envelope
// // { code, data, message } — we unwrap `data` here so UI never sees it.
//
// const API_BASE = "/api/operator";
//
// export type AcknowledgeResponse = {
//   tripId: string;
//   status: TripStatus;
//   acknowledgedAt: string;
// };
//
// export async function acknowledgeTrip(tripId: string): Promise<AcknowledgeResponse> {
//   const res = await fetch(`${API_BASE}/trips/${tripId}/acknowledge`, {
//     method: "POST",
//     credentials: "include",
//     headers: { "Content-Type": "application/json" },
//     body: JSON.stringify({ acknowledgedAt: new Date().toISOString() }),
//   });
//   if (!res.ok) {
//     const error = await res.json().catch(() => ({}));
//     throw new Error(error.message ?? `Acknowledge failed: ${res.status}`);
//   }
//   const envelope = await res.json();
//   return envelope.data;   // unwrap
// }
