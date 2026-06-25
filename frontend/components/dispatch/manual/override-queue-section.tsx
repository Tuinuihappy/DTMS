"use client";

import { useState } from "react";
import {
  approveOverride,
  denyOverride,
  type OperatorBoardRow,
  type OverrideQueueRow,
} from "@/lib/api/admin-manual";

// Phase 4.6 — Right column: pending geofence override requests.
// Each row exposes Approve + Deny actions that hit the admin endpoints
// + push a hint through SignalR (the broadcast triggers the parent
// page's refetch via the hub handler).
//
// "Decided by" is the dispatcher's own Operator.Id. For Phase 4.6 MVP
// we use a hardcoded placeholder — once a /me query for dispatcher
// sessions lands (it's an easy follow-up), the parent component will
// pass it in. The backend still records the decision either way.
type Props = {
  overrides: OverrideQueueRow[];
  operators: OperatorBoardRow[];
  onChange: () => void;
};

// Use the first Admin operator as the "decided by" stand-in.
// Hard-coding here keeps the UI shippable without an extra /me round
// trip; the supervisor Id is informational on the audit row, not load-
// bearing for authorization (the endpoint still requires the admin
// JWT cookie).
function pickDispatcherId(operators: OperatorBoardRow[]): string | null {
  return operators.find((o) => o.role === "Admin" || o.role === "Supervisor")?.id ?? null;
}

export function OverrideQueueSection({ overrides, operators, onChange }: Props) {
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [denyTarget, setDenyTarget] = useState<OverrideQueueRow | null>(null);

  const decidedBy = pickDispatcherId(operators);

  const operatorByEmployeeId = (id: string) =>
    operators.find((o) => o.id === id)?.employeeCode ?? id.slice(0, 8);

  const onApprove = async (row: OverrideQueueRow) => {
    if (!decidedBy) {
      setError("No Admin or Supervisor operator registered yet — sign in once as one to bootstrap.");
      return;
    }
    setBusyId(row.id);
    setError(null);
    try {
      await approveOverride(row.id, decidedBy, null);
      onChange();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Approve failed.");
    } finally {
      setBusyId(null);
    }
  };

  return (
    <section className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-950">
      <header className="border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
        <h2 className="text-sm font-medium uppercase tracking-wide text-zinc-500">
          Override queue ({overrides.length})
        </h2>
      </header>

      {error && (
        <div className="mx-4 mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200">
          {error}
        </div>
      )}

      {overrides.length === 0 ? (
        <div className="p-6 text-center text-sm text-zinc-500">
          No pending override requests.
        </div>
      ) : (
        <ul className="divide-y divide-zinc-200 dark:divide-zinc-800">
          {overrides.map((row) => (
            <li key={row.id} className="px-4 py-3">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="text-sm">
                    <span className="font-mono">{operatorByEmployeeId(row.operatorId)}</span>
                    <span className="ml-2 text-xs text-zinc-500">
                      trip {row.tripId.slice(0, 8)}
                    </span>
                  </div>
                  <div className="mt-1 text-xs text-zinc-600 dark:text-zinc-400">
                    {row.reason}
                  </div>
                  <div className="mt-1 text-xs text-zinc-500">
                    {row.distanceFromGeofenceM.toFixed(0)}m outside · requested{" "}
                    {new Date(row.requestedAt).toLocaleTimeString()} · expires{" "}
                    {new Date(row.expiresAt).toLocaleTimeString()}
                  </div>
                </div>
                <div className="flex shrink-0 gap-2">
                  <button
                    type="button"
                    onClick={() => onApprove(row)}
                    disabled={busyId !== null}
                    className="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
                  >
                    Approve
                  </button>
                  <button
                    type="button"
                    onClick={() => setDenyTarget(row)}
                    disabled={busyId !== null}
                    className="rounded-lg border border-zinc-300 px-3 py-1.5 text-xs text-zinc-700 hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
                  >
                    Deny
                  </button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      {denyTarget && (
        <DenyDialog
          row={denyTarget}
          decidedBy={decidedBy}
          onClose={() => setDenyTarget(null)}
          onSuccess={() => {
            setDenyTarget(null);
            onChange();
          }}
        />
      )}
    </section>
  );
}

function DenyDialog({
  row,
  decidedBy,
  onClose,
  onSuccess,
}: {
  row: OverrideQueueRow;
  decidedBy: string | null;
  onClose: () => void;
  onSuccess: () => void;
}) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (!decidedBy) {
      setError("Need a Supervisor or Admin operator to deny.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await denyOverride(row.id, decidedBy, reason);
      onSuccess();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Deny failed.");
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
        <h3 className="text-base font-semibold">Deny override</h3>
        <p className="mt-1 text-xs text-zinc-500">
          Override for trip <span className="font-mono">{row.tripId.slice(0, 8)}</span>.
          Reason is shown to the operator.
        </p>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={4}
          placeholder="e.g. Operator must approach the loading dock — current location is in the parking lot."
          className="mt-3 w-full rounded-md border border-zinc-300 bg-white px-2 py-1.5 text-sm dark:border-zinc-700 dark:bg-zinc-950"
        />
        {error && (
          <div className="mt-3 rounded-md border border-red-200 bg-red-50 px-2 py-1.5 text-xs text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200">
            {error}
          </div>
        )}
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
            disabled={busy || reason.trim().length < 3}
            className="rounded-md bg-red-600 px-3 py-1.5 text-sm text-white disabled:opacity-50"
          >
            {busy ? "Denying…" : "Confirm deny"}
          </button>
        </div>
      </div>
    </div>
  );
}
