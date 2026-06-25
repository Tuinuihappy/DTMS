"use client";

import { useState } from "react";
import {
  reassignManualTrip,
  type ManualTripBoardRow,
  type OperatorBoardRow,
} from "@/lib/api/admin-manual";

// Phase 4.6 — Left column: operators with their current trip + status.
// Each row gets a "Reassign" button that opens an inline dialog letting
// dispatcher swap the trip to another active+idle operator.
type Props = {
  operators: OperatorBoardRow[];
  trips: ManualTripBoardRow[];
  onChange: () => void;
};

export function OperatorBoardSection({ operators, trips, onChange }: Props) {
  const tripByOperator = new Map(trips.map((t) => [t.operatorId, t]));
  const [reassignTripId, setReassignTripId] = useState<string | null>(null);

  const idleOperators = operators.filter(
    (o) => o.status === "Active" && o.currentTripId === null,
  );

  return (
    <section className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-950">
      <header className="border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
        <h2 className="text-sm font-medium uppercase tracking-wide text-zinc-500">
          Operators ({operators.length})
        </h2>
      </header>
      {operators.length === 0 ? (
        <div className="p-6 text-center text-sm text-zinc-500">
          No operators yet. Operators are created automatically the first time they sign in.
        </div>
      ) : (
        <ul className="divide-y divide-zinc-200 dark:divide-zinc-800">
          {operators.map((op) => {
            const trip = tripByOperator.get(op.id);
            return (
              <li key={op.id} className="flex items-center justify-between px-4 py-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-sm text-zinc-700 dark:text-zinc-200">
                      {op.employeeCode}
                    </span>
                    <StatusBadge status={op.status} />
                    <RoleBadge role={op.role} />
                  </div>
                  <div className="mt-0.5 text-xs text-zinc-500">
                    {op.displayName}
                    {op.primaryWarehouseId
                      ? ` · WH ${op.primaryWarehouseId.slice(0, 8)}`
                      : ""}
                  </div>
                  {trip && (
                    <div className="mt-1 text-xs">
                      <span className="text-zinc-500">Trip </span>
                      <span className="font-mono text-zinc-700 dark:text-zinc-200">
                        {trip.tripId.slice(0, 8)}
                      </span>{" "}
                      · {tripStageLabel(trip)}
                    </div>
                  )}
                </div>
                {trip && trip.droppedAt === null && (
                  <button
                    type="button"
                    onClick={() => setReassignTripId(trip.tripId)}
                    className="rounded-lg border border-zinc-300 bg-white px-2.5 py-1 text-xs text-zinc-700 hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300 dark:hover:bg-zinc-800"
                  >
                    Reassign
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {reassignTripId && (
        <ReassignDialog
          tripId={reassignTripId}
          idleOperators={idleOperators}
          currentOperatorId={
            trips.find((t) => t.tripId === reassignTripId)?.operatorId ?? null
          }
          onClose={() => setReassignTripId(null)}
          onSuccess={() => {
            setReassignTripId(null);
            onChange();
          }}
        />
      )}
    </section>
  );
}

function StatusBadge({ status }: { status: OperatorBoardRow["status"] }) {
  const map: Record<OperatorBoardRow["status"], string> = {
    Active: "bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300",
    OnLeave: "bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300",
    Deactivated: "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400",
  };
  return (
    <span className={`rounded-full px-2 py-0.5 text-[10px] font-medium ${map[status]}`}>
      {status}
    </span>
  );
}

function RoleBadge({ role }: { role: OperatorBoardRow["role"] }) {
  if (role === "Operator") return null;
  return (
    <span className="rounded-full bg-blue-50 px-2 py-0.5 text-[10px] font-medium text-blue-700 dark:bg-blue-500/15 dark:text-blue-300">
      {role}
    </span>
  );
}

function tripStageLabel(trip: ManualTripBoardRow): string {
  if (trip.droppedAt) return "delivered";
  if (trip.pickedUpAt) return "in transit";
  if (trip.acknowledgedAt) return "en route to pickup";
  return "awaiting acknowledge";
}

function ReassignDialog({
  tripId,
  idleOperators,
  currentOperatorId,
  onClose,
  onSuccess,
}: {
  tripId: string;
  idleOperators: OperatorBoardRow[];
  currentOperatorId: string | null;
  onClose: () => void;
  onSuccess: () => void;
}) {
  const [newOperatorId, setNewOperatorId] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const targets = idleOperators.filter((o) => o.id !== currentOperatorId);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await reassignManualTrip(tripId, newOperatorId, reason || null);
      onSuccess();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Reassign failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl dark:bg-zinc-900"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-base font-semibold">Reassign trip</h3>
        <p className="mt-1 text-xs text-zinc-500">
          Trip <span className="font-mono">{tripId.slice(0, 8)}</span> — pick a different active + idle operator.
        </p>
        <div className="mt-4 flex flex-col gap-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-zinc-500">New operator</span>
            <select
              value={newOperatorId}
              onChange={(e) => setNewOperatorId(e.target.value)}
              disabled={busy}
              className="h-10 rounded-md border border-zinc-300 bg-white px-2 dark:border-zinc-700 dark:bg-zinc-950"
            >
              <option value="">— choose —</option>
              {targets.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.employeeCode} · {o.displayName}
                </option>
              ))}
            </select>
            {targets.length === 0 && (
              <span className="text-xs text-amber-700 dark:text-amber-300">
                No idle Active operators available right now.
              </span>
            )}
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-zinc-500">Reason (optional)</span>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              disabled={busy}
              rows={3}
              className="rounded-md border border-zinc-300 bg-white px-2 py-1.5 dark:border-zinc-700 dark:bg-zinc-950"
            />
          </label>
          {error && (
            <div className="rounded-md border border-red-200 bg-red-50 px-2 py-1.5 text-xs text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200">
              {error}
            </div>
          )}
        </div>
        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 dark:border-zinc-700 dark:text-zinc-300"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={busy || !newOperatorId}
            className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm text-white disabled:opacity-50 dark:bg-zinc-100 dark:text-zinc-900"
          >
            {busy ? "Reassigning…" : "Confirm reassign"}
          </button>
        </div>
      </div>
    </div>
  );
}
