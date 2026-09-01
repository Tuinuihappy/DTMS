"use client";

import { MoreHorizontal } from "lucide-react";
import { useRef, useState } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import { RowMenuPortal } from "@/components/primitives/row-menu-portal";
import type { Carrier } from "@/lib/api/fleet-carriers";
import { Permissions } from "@/lib/auth/permissions";
import { cn } from "@/lib/utils";
import type { CarrierAction } from "./carrier-action-dialog";

/**
 * Which actions a carrier offers, by status — the same table the endpoints
 * enforce (ADR-019 P1.2). Kept as data in one place so the menu cannot drift
 * into offering something the server will reject: a visible button that always
 * 400s is worse than no button.
 *
 * Delete appears whenever the carrier is Available, because whether history
 * blocks it is something only the server knows. A refusal comes back as 409
 * carrying the reason, which the dialog shows verbatim.
 */
const ALLOWED: Record<Carrier["status"], readonly (CarrierAction | "edit" | "history")[]> = {
  Available: ["edit", "location", "maintenance", "retire", "delete", "history"],
  // Bound to a trip (P3). Only its location can move; everything else waits.
  InUse: ["location", "history"],
  Maintenance: ["edit", "location", "return-to-service", "retire", "history"],
  // Never a dead end — un-retire is the way back out.
  Retired: ["edit", "location", "unretire", "history"],
};

export function CarrierRowMenu({
  carrier,
  onAction,
  onShowHistory,
  onShowPhotos,
  onEdit,
}: {
  carrier: Carrier;
  onAction: (action: CarrierAction) => void;
  onShowHistory: () => void;
  onShowPhotos: () => void;
  onEdit: () => void;
}) {
  const { hasPermission } = useAuth();
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const allowed = ALLOWED[carrier.status];
  const can = (a: (typeof allowed)[number]) => allowed.includes(a);

  const canWrite = hasPermission(Permissions.Fleet.CarrierWrite);
  const canMaintain = hasPermission(Permissions.Fleet.CarrierMaintain);
  const canDelete = hasPermission(Permissions.Fleet.CarrierDelete);

  const pick = (fn: () => void) => {
    setOpen(false);
    fn();
  };

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="rounded-full p-1.5 text-[var(--color-ink-500)] transition-colors hover:bg-white/50 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
        aria-label={`Actions for ${carrier.carrierCode}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <MoreHorizontal className="h-4 w-4" strokeWidth={2.2} />
      </button>

      <RowMenuPortal
        open={open}
        onClose={() => setOpen(false)}
        triggerRef={triggerRef}
        width={216}
        ariaLabel={`Actions for ${carrier.carrierCode}`}
      >
        <MenuItem onClick={() => pick(onShowHistory)}>Maintenance history</MenuItem>

        {/* Available in every status, like history — a retired carrier's
            photos are still worth looking at. */}
        <MenuItem onClick={() => pick(onShowPhotos)}>Photos</MenuItem>

        {can("edit") && canWrite && <MenuItem onClick={() => pick(onEdit)}>Edit details</MenuItem>}

        {can("location") && canWrite && (
          <MenuItem onClick={() => pick(() => onAction("location"))}>Update location</MenuItem>
        )}

        {can("maintenance") && canMaintain && (
          <MenuItem onClick={() => pick(() => onAction("maintenance"))}>
            Send for maintenance
          </MenuItem>
        )}

        {can("return-to-service") && canMaintain && (
          <MenuItem tone="brand" onClick={() => pick(() => onAction("return-to-service"))}>
            Return to service
          </MenuItem>
        )}

        {can("unretire") && canWrite && (
          <MenuItem tone="brand" onClick={() => pick(() => onAction("unretire"))}>
            Bring back into use
          </MenuItem>
        )}

        {can("retire") && canWrite && (
          <MenuItem onClick={() => pick(() => onAction("retire"))}>Retire</MenuItem>
        )}

        {can("delete") && canDelete && (
          <MenuItem tone="danger" onClick={() => pick(() => onAction("delete"))}>
            Delete permanently
          </MenuItem>
        )}
      </RowMenuPortal>
    </>
  );
}

function MenuItem({
  tone = "default",
  onClick,
  children,
}: {
  tone?: "default" | "brand" | "danger";
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className={cn(
        "block w-full px-3 py-2 text-left text-[12px] font-medium transition-colors",
        tone === "danger"
          ? "text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]"
          : tone === "brand"
            ? "text-[var(--color-brand-900)] hover:bg-[var(--color-pastel-mint)] dark:text-[var(--color-brand-500)]"
            : "text-[var(--color-ink-700)] hover:bg-white/60 dark:hover:bg-white/[0.08]",
      )}
    >
      {children}
    </button>
  );
}
