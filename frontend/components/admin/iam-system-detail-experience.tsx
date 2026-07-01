"use client";

import {
  AlertCircle,
  ArrowLeft,
  Check,
  CheckCircle2,
  Copy,
  KeyRound,
  Pencil,
  Power,
  PowerOff,
  RefreshCw,
  Settings2,
  X,
  XCircle,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  activateSystem,
  deactivateSystem,
  getSystem,
  grantSystemPermission,
  issueToken,
  listIssuedTokens,
  patchSystem,
  revokeSystemPermission,
  revokeToken,
  rotateCredential,
  setCallback,
  type CallbackConfigRequest,
  type CredentialSummary,
  type IssuedTokenSummary,
  type SystemDetailDto,
} from "@/lib/api/iam-systems";
import { listPermissions, type PermissionDto } from "@/lib/api/iam";
import { resolveStandardSystemPermissions } from "@/lib/iam/standard-system-permissions";
import { OneTimeSecretBanner } from "@/components/admin/one-time-secret-banner";
import { PermissionsChecklist } from "@/components/admin/permissions-checklist";
import { cn } from "@/lib/utils";

// Phase S.6 — single-system detail page. Three cards: metadata,
// credential + rotate, callback config. Subscriptions live on a
// separate page (drill-down nav) — link out from the bottom of this
// page. Edit flows open inline modals styled like the create modal on
// the list page.

export function IamSystemDetailExperience({ systemKey }: { systemKey: string }) {
  const [data, setData] = useState<SystemDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editingMetadata, setEditingMetadata] = useState(false);
  const [editingCallback, setEditingCallback] = useState(false);
  // Codes currently in flight (grant or revoke). Drives per-row spinner.
  const [togglingCodes, setTogglingCodes] = useState<Set<string>>(new Set());
  const [catalog, setCatalog] = useState<PermissionDto[] | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [rotatedKey, setRotatedKey] = useState<{
    secret: string;
    rotatedAt: string;
  } | null>(null);
  // Admin-issued long-lived JWT — separate from rotatedKey because it's
  // a different artifact (JWT to paste directly, not a client_secret to
  // exchange via /oauth/token).
  const [issuedToken, setIssuedToken] = useState<{
    accessToken: string;
    expiresAt: string;
    expiresInSeconds: number;
  } | null>(null);
  const [issuingToken, setIssuingToken] = useState(false);
  const [showIssueModal, setShowIssueModal] = useState(false);
  // Phase S.8c — list of admin-issued JWTs (audit + revocation UI).
  // Refetched on mount, on successful Issue, and after Revoke.
  const [issuedTokens, setIssuedTokens] = useState<IssuedTokenSummary[]>([]);
  const [revokingJti, setRevokingJti] = useState<string | null>(null);
  // Which JTI was just copied — drives the "✓ copied" glyph for 1.5s.
  const [copiedJti, setCopiedJti] = useState<string | null>(null);

  // Synthesize the runtime-resolved standard system permission rows
  // (`dtms:source:{key}:order:read|write`) so they appear under a
  // "Source" group in the checklist alongside catalog perms.
  const syntheticSystemPerms = useMemo<PermissionDto[]>(
    () =>
      resolveStandardSystemPermissions(systemKey).map((code) => ({
        code,
        description: "Standard source-system permission (auto-seeded at create)",
        module: "Source",
      })),
    [systemKey],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await getSystem(systemKey));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [systemKey]);

  useEffect(() => {
    void load();
  }, [load]);

  const loadIssuedTokens = useCallback(async () => {
    try {
      setIssuedTokens(await listIssuedTokens(systemKey));
    } catch {
      // Non-fatal — table shows empty; page keeps loading the rest.
      // Explicit error banner is overkill for a supplemental view.
      setIssuedTokens([]);
    }
  }, [systemKey]);

  useEffect(() => {
    void loadIssuedTokens();
  }, [loadIssuedTokens]);

  // Load the global permission catalog once when the page mounts. The
  // checkbox list below renders catalog rows × this system; if catalog
  // fetch fails, the standard templates resolved from the system key
  // are still shown so the page is never empty.
  useEffect(() => {
    const abort = new AbortController();
    listPermissions(abort.signal)
      .then((rows) => setCatalog(rows))
      .catch((e) => {
        if (!abort.signal.aborted) setCatalogError((e as Error).message);
      });
    return () => abort.abort();
  }, []);

  const onActivate = async () => {
    setBusy(true);
    try {
      await activateSystem(systemKey);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onDeactivate = async () => {
    if (!confirm(`Deactivate "${systemKey}"? Inbound auth will return 401.`)) return;
    setBusy(true);
    try {
      await deactivateSystem(systemKey);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onTogglePermission = async (code: string, currentlyGranted: boolean) => {
    setTogglingCodes((prev) => new Set(prev).add(code));
    setError(null);
    try {
      if (currentlyGranted) {
        await revokeSystemPermission(systemKey, code);
      } else {
        await grantSystemPermission(systemKey, code);
      }
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setTogglingCodes((prev) => {
        const next = new Set(prev);
        next.delete(code);
        return next;
      });
    }
  };

  const onRotate = async () => {
    if (rotatedKey) {
      setError("A freshly rotated key is still visible above — copy it before rotating again.");
      return;
    }
    if (!confirm(
      `Rotate the API key for "${systemKey}"?\n\n` +
      `• The current key stops working the moment you click OK.\n` +
      `• A NEW key will be shown ONCE in a banner — copy it then test it.\n` +
      `• Do NOT rotate again until you have copied + verified the new key.`,
    )) return;
    setBusy(true);
    try {
      const r = await rotateCredential(systemKey);
      setRotatedKey({ secret: r.secret, rotatedAt: r.rotatedAt });
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onIssueToken = async (lifetimeSeconds: number) => {
    setIssuingToken(true);
    setError(null);
    try {
      const r = await issueToken(systemKey, { lifetimeSeconds });
      setIssuedToken({
        accessToken: r.accessToken,
        expiresAt: r.expiresAt,
        expiresInSeconds: r.expiresInSeconds,
      });
      setShowIssueModal(false);
      // Refresh the issued-tokens list so the new row shows up
      // immediately (otherwise operator sees the banner but no row).
      void loadIssuedTokens();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setIssuingToken(false);
    }
  };

  const onCopyJti = async (jti: string) => {
    try {
      await navigator.clipboard.writeText(jti);
      setCopiedJti(jti);
      // Auto-clear the ✓ glyph so a stale "copied" state doesn't
      // confuse the next click.
      setTimeout(() => setCopiedJti(c => (c === jti ? null : c)), 1500);
    } catch {
      /* clipboard blocked (rare) — user can still select+copy the text */
    }
  };

  const onRevokeToken = async (jti: string) => {
    if (!confirm(
      `Revoke this token?\n\n` +
      `• The token stops authenticating on its NEXT request (revocation is immediate).\n` +
      `• Other tokens for "${systemKey}" are unaffected.\n` +
      `• This cannot be undone — the partner will need a new token.`,
    )) return;
    setRevokingJti(jti);
    setError(null);
    try {
      await revokeToken(systemKey, jti);
      await loadIssuedTokens();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setRevokingJti(null);
    }
  };

  if (loading && !data) {
    return <div className="px-2 py-6 text-sm text-[var(--color-ink-400)]">Loading…</div>;
  }
  if (!data) {
    return (
      <div className="px-2 py-6">
        <p className="text-sm text-rose-600">{error ?? "System not found"}</p>
        <Link href="/admin/systems" className="mt-2 inline-block text-xs text-[var(--color-brand-600)] hover:underline">
          ← Back to systems
        </Link>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <Link
          href="/admin/systems"
          className="inline-flex items-center gap-1 text-[11.5px] text-[var(--color-ink-500)] hover:text-[var(--color-ink-700)]"
        >
          <ArrowLeft className="h-3 w-3" strokeWidth={2.2} />
          All systems
        </Link>
        <div className="flex items-end justify-between gap-3">
          <div>
            <div className="flex items-baseline gap-3">
              <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">{data.displayName}</h1>
              <code className="font-mono text-[12px] text-[var(--color-ink-500)]">{data.key}</code>
            </div>
            <div className="mt-1 flex items-center gap-3 text-[12px] text-[var(--color-ink-500)]">
              <span
                className={cn(
                  "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-medium",
                  data.isActive
                    ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300"
                    : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
                )}
              >
                {data.isActive ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />}
                {data.isActive ? "Active" : "Inactive"}
              </span>
              <span>Created {new Date(data.createdAt).toLocaleDateString()}</span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={load}
              disabled={busy || loading}
              className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white disabled:opacity-50 dark:bg-white/[0.05]"
            >
              <RefreshCw className={cn("h-3 w-3", (busy || loading) && "animate-spin")} strokeWidth={2.4} />
              Refresh
            </button>
            {data.isActive ? (
              <button
                type="button"
                disabled={busy}
                onClick={onDeactivate}
                className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] disabled:opacity-50"
              >
                <PowerOff className="h-3 w-3" strokeWidth={2.2} />
                Deactivate
              </button>
            ) : (
              <button
                type="button"
                disabled={busy}
                onClick={onActivate}
                className="inline-flex items-center gap-1 rounded-full border border-emerald-300 bg-emerald-50 px-3 py-1.5 text-[11.5px] font-medium text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"
              >
                <Power className="h-3 w-3" strokeWidth={2.2} />
                Activate
              </button>
            )}
          </div>
        </div>
      </header>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
          <span>{error}</span>
          <button type="button" onClick={() => setError(null)} className="ml-auto" aria-label="Dismiss">
            <X className="h-3 w-3" />
          </button>
        </div>
      )}

      {rotatedKey && (
        <OneTimeSecretBanner
          title={`New client_secret for "${data.key}" — copy now (old value revoked)`}
          secret={rotatedKey.secret}
          testKeyForSystem={data.key}
          helpText={`Rotated at ${rotatedKey.rotatedAt}. Click "Test this credential" to confirm it authenticates, then send to the operator. Do NOT click Rotate again until you've copied + tested.`}
          onDismiss={() => setRotatedKey(null)}
        />
      )}

      {issuedToken && (
        <OneTimeSecretBanner
          title={`Admin-issued JWT for "${data.key}" — copy now`}
          secret={issuedToken.accessToken}
          testKeyForSystem={data.key}
          testMode="jwt"
          helpText={
            `Expires ${new Date(issuedToken.expiresAt).toLocaleString()} ` +
            `(in ${Math.round(issuedToken.expiresInSeconds / 86400)} days). ` +
            `Send to the partner via a secure channel — they use it directly as ` +
            `"Authorization: Bearer <token>" with no /oauth/token round-trip needed. ` +
            `Revocation requires deactivating the system or rotating the signing keypair ` +
            `until per-jti revocation lands.`
          }
          onDismiss={() => setIssuedToken(null)}
        />
      )}

      {showIssueModal && (
        <IssueTokenModal
          systemKey={data.key}
          submitting={issuingToken}
          onCancel={() => setShowIssueModal(false)}
          onIssue={onIssueToken}
        />
      )}

      {/* ── Metadata card ───────────────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Metadata</h2>
          <button
            type="button"
            onClick={() => setEditingMetadata(true)}
            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            <Pencil className="h-3 w-3" strokeWidth={2.2} />
            Edit
          </button>
        </div>
        <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-[12px]">
          <DefRow label="Description" value={data.description ?? "—"} />
          <DefRow label="Owner contact" value={data.ownerContact ?? "—"} />
        </dl>
      </section>

      {/* ── Credential card ─────────────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Credential</h2>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setShowIssueModal(true)}
              disabled={busy || issuedToken !== null || !data.isActive}
              title={
                !data.isActive
                  ? "Reactivate the system first"
                  : issuedToken
                  ? "Copy + dismiss the visible token first"
                  : "Issue a long-lived JWT for partners that can't run an OAuth client"
              }
              className="inline-flex items-center gap-1 rounded border border-sky-300 bg-sky-50 px-2 py-1 text-[11px] text-sky-700 hover:bg-sky-100 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <KeyRound className="h-3 w-3" strokeWidth={2.2} />
              Issue JWT
            </button>
            <button
              type="button"
              onClick={onRotate}
              disabled={busy || rotatedKey !== null}
              title={rotatedKey ? "Copy + test the visible key first before rotating again" : undefined}
              className="inline-flex items-center gap-1 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-[11px] text-amber-700 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <KeyRound className="h-3 w-3" strokeWidth={2.2} />
              Rotate key
            </button>
          </div>
        </div>
        <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
          The current key works forever. Rotate <em>only</em> when the key
          was leaked, someone who held it left, or a policy mandates it —
          rotating revokes the existing key immediately and you must
          re-distribute the new one to the source system.
        </p>
        {data.credential ? (
          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-[12px]">
            <DefRow label="Auth scheme (inbound)" value={data.credential.authScheme} mono />
            <DefRow label="Last updated" value={new Date(data.credential.updatedAt).toLocaleString()} />
          </dl>
        ) : (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">No credential row.</p>
        )}
      </section>

      {/* ── Issued tokens card (Phase S.8c) ─────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">
            Issued tokens <span className="ml-1 text-[11px] font-normal text-[var(--color-ink-400)]">({issuedTokens.length})</span>
          </h2>
        </div>
        <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
          Every click of <strong>Issue JWT</strong> is recorded here. Revoking a
          row invalidates that token immediately on its next request — other
          tokens for this system are unaffected. Rows past expiry stay for audit.
        </p>
        {issuedTokens.length === 0 ? (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">No admin-issued tokens.</p>
        ) : (
          <div className="mt-3 overflow-hidden rounded border border-[var(--color-ink-100)] dark:border-white/[0.06]">
            <table className="w-full text-left text-[12px]">
              <thead className="bg-[var(--color-ink-50)] text-[10.5px] uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.04]">
                <tr>
                  <th className="px-3 py-2">JTI</th>
                  <th className="px-3 py-2">Issued</th>
                  <th className="px-3 py-2">Expires</th>
                  <th className="px-3 py-2">By</th>
                  <th className="px-3 py-2">Status</th>
                  <th className="px-3 py-2 text-right"></th>
                </tr>
              </thead>
              <tbody>
                {issuedTokens.map((t) => {
                  const isExpired = new Date(t.expiresAt) < new Date();
                  const isRevoked = t.status === "Revoked";
                  return (
                    <tr key={t.jti} className="border-t border-[var(--color-ink-100)] dark:border-white/[0.06]">
                      <td className="px-3 py-2 font-mono text-[11px]" title={t.jti}>
                        <span className="inline-flex items-center gap-1.5">
                          <span>{t.jti.slice(0, 8)}…{t.jti.slice(-4)}</span>
                          <button
                            type="button"
                            onClick={() => onCopyJti(t.jti)}
                            title="Copy full JTI"
                            aria-label="Copy full JTI"
                            className="rounded p-0.5 text-[var(--color-ink-400)] hover:bg-[var(--color-ink-50)] hover:text-[var(--color-ink-600)] dark:hover:bg-white/[0.06]"
                          >
                            {copiedJti === t.jti
                              ? <Check className="h-3 w-3 text-emerald-600" strokeWidth={2.4} />
                              : <Copy className="h-3 w-3" strokeWidth={2} />}
                          </button>
                        </span>
                      </td>
                      <td className="px-3 py-2 text-[var(--color-ink-500)]">{new Date(t.issuedAt).toLocaleString()}</td>
                      <td className="px-3 py-2 text-[var(--color-ink-500)]">{new Date(t.expiresAt).toLocaleString()}</td>
                      <td className="px-3 py-2 text-[var(--color-ink-500)]">{t.issuedBy}</td>
                      <td className="px-3 py-2">
                        <span className={cn(
                          "inline-flex rounded px-1.5 py-0.5 text-[10.5px] font-medium",
                          isRevoked
                            ? "bg-rose-100 text-rose-700 dark:bg-rose-950 dark:text-rose-300"
                            : isExpired
                            ? "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]"
                            : "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300",
                        )}>
                          {isRevoked ? "Revoked" : isExpired ? "Expired" : "Active"}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-right">
                        {isRevoked || isExpired ? (
                          <span className="text-[10.5px] text-[var(--color-ink-300)]">—</span>
                        ) : (
                          <button
                            type="button"
                            onClick={() => onRevokeToken(t.jti)}
                            disabled={revokingJti === t.jti}
                            className="rounded border border-rose-300 bg-rose-50 px-2 py-1 text-[11px] text-rose-700 hover:bg-rose-100 disabled:opacity-50 dark:border-rose-500/40 dark:bg-rose-950 dark:text-rose-300 dark:hover:bg-rose-900"
                          >
                            {revokingJti === t.jti ? "Revoking…" : "Revoke"}
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* ── Callback card ───────────────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Outbound callback</h2>
          <button
            type="button"
            onClick={() => setEditingCallback(true)}
            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            <Settings2 className="h-3 w-3" strokeWidth={2.2} />
            Configure
          </button>
        </div>
        {data.credential?.hasCallbackBaseUrl ? (
          <>
            <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-[12px]">
              <DefRow label="Base URL" value={data.credential.callbackBaseUrl ?? "—"} mono />
              <DefRow label="Auth scheme" value={data.credential.callbackAuthScheme ?? "—"} mono />
              <DefRow label="Timeout (ms)" value={String(data.credential.callbackTimeoutMs)} />
            </dl>
            {data.credential.callbackTokenExpiresAt && (
              <TokenExpiryBadge expiresAt={data.credential.callbackTokenExpiresAt} />
            )}
          </>
        ) : (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">
            No callback URL set. DTMS will not call this system back when its orders complete.
          </p>
        )}
      </section>

      {/* ── Subscriptions summary + link to drill-down ──────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Event subscriptions</h2>
          <Link
            href={`/admin/systems/${encodeURIComponent(data.key)}/subscriptions`}
            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            Manage subscriptions →
          </Link>
        </div>
        {data.subscriptions.length === 0 ? (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">
            No subscriptions. DTMS will not fan out any events to this system until at least one is added.
          </p>
        ) : (
          <ul className="mt-3 space-y-1 text-[12px]">
            {data.subscriptions.map((s) => (
              <li key={s.eventType} className="flex items-center gap-2">
                <code className="font-mono text-[11.5px]">{s.eventType}</code>
                <span className="text-[var(--color-ink-400)]">→</span>
                <code className="font-mono text-[11.5px] text-[var(--color-ink-500)]">{s.payloadFormatKey}</code>
                <span
                  className={cn(
                    "ml-auto inline-flex rounded-full px-2 py-0.5 text-[10px] font-medium",
                    s.enabled
                      ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300"
                      : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
                  )}
                >
                  {s.enabled ? "Enabled" : "Disabled"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* ── Granted permissions (Phase S.7 — checkbox grid) ───────── */}
      <PermissionsChecklist
        granted={data.permissions}
        catalog={catalog}
        catalogError={catalogError}
        toggling={togglingCodes}
        onToggle={onTogglePermission}
        syntheticPermissions={syntheticSystemPerms}
        hint="Tick a row to grant; untick to revoke. Changes apply on the next inbound request — no cache to flush. To stop this system entirely, use Deactivate above."
      />

      {editingMetadata && (
        <EditMetadataModal
          initial={data}
          onClose={() => setEditingMetadata(false)}
          onSaved={() => {
            setEditingMetadata(false);
            void load();
          }}
        />
      )}
      {editingCallback && (
        <ConfigureCallbackModal
          systemKey={data.key}
          initial={data.credential}
          onClose={() => setEditingCallback(false)}
          onSaved={() => {
            setEditingCallback(false);
            void load();
          }}
        />
      )}
    </div>
  );
}

// ── Subcomponents ───────────────────────────────────────────────────────

/**
 * Phase S.6 follow-up — surfaces the JWT `exp` claim DTMS decoded from
 * the stored bearer token. Color-coded by remaining time:
 *
 *  > 30 days   → neutral info
 *  ≤ 30 days   → amber warning
 *  ≤ 7 days    → red warning
 *  past exp    → red "EXPIRED" badge
 *
 * Operators rotate the OMS-issued bearer token by calling OMS to mint
 * a new one + saving it via "Configure callback". This badge is the
 * heads-up they need to do that before traffic starts 401-ing.
 */
function TokenExpiryBadge({ expiresAt }: { expiresAt: string }) {
  const expDate = new Date(expiresAt);
  const now = new Date();
  const msRemaining = expDate.getTime() - now.getTime();
  const dayMs = 24 * 60 * 60 * 1000;
  const daysRemaining = Math.floor(msRemaining / dayMs);

  let color: string;
  let label: string;
  if (msRemaining < 0) {
    color = "border-rose-400 bg-rose-50 text-rose-800 dark:border-rose-500/40 dark:bg-rose-950/30 dark:text-rose-300";
    label = `EXPIRED ${Math.abs(daysRemaining)}d ago — rotate immediately`;
  } else if (daysRemaining <= 7) {
    color = "border-rose-400 bg-rose-50 text-rose-800 dark:border-rose-500/40 dark:bg-rose-950/30 dark:text-rose-300";
    label = `Expires in ${daysRemaining} day${daysRemaining === 1 ? "" : "s"} — rotate soon`;
  } else if (daysRemaining <= 30) {
    color = "border-amber-400 bg-amber-50 text-amber-800 dark:border-amber-500/40 dark:bg-amber-950/30 dark:text-amber-300";
    label = `Expires in ${daysRemaining} days`;
  } else {
    color = "border-[var(--color-ink-200)] bg-[var(--color-surface-1)] text-[var(--color-ink-600)]";
    label = `Expires in ${daysRemaining} days (${expDate.toLocaleDateString()})`;
  }

  return (
    <div className={`mt-3 inline-flex items-center gap-2 rounded border px-3 py-1.5 text-[11.5px] ${color}`}>
      <span className="font-semibold">Outbound token:</span>
      <span>{label}</span>
    </div>
  );
}

function DefRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <>
      <dt className="text-[10.5px] uppercase tracking-[0.08em] text-[var(--color-ink-400)]">{label}</dt>
      <dd className={cn("text-[var(--color-ink-700)]", mono && "font-mono text-[11.5px]")}>{value}</dd>
    </>
  );
}

function EditMetadataModal({
  initial,
  onClose,
  onSaved,
}: {
  initial: SystemDetailDto;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [displayName, setDisplayName] = useState(initial.displayName);
  const [description, setDescription] = useState(initial.description ?? "");
  const [ownerContact, setOwnerContact] = useState(initial.ownerContact ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await patchSystem(initial.key, {
        displayName: displayName || null,
        description,
        ownerContact,
      });
      onSaved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ModalShell title="Edit metadata" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Display name">
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
        <Field label="Description">
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
        <Field label="Owner contact">
          <input
            value={ownerContact}
            onChange={(e) => setOwnerContact(e.target.value)}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
      </div>
      {error && <ErrorBanner message={error} />}
      <ModalFooter onCancel={onClose} onSave={submit} saving={submitting} />
    </ModalShell>
  );
}

function ConfigureCallbackModal({
  systemKey,
  initial,
  onClose,
  onSaved,
}: {
  systemKey: string;
  initial: CredentialSummary | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [callbackBaseUrl, setCallbackBaseUrl] = useState(initial?.callbackBaseUrl ?? "");
  const [callbackAuthScheme, setCallbackAuthScheme] = useState<string>(
    initial?.callbackAuthScheme ?? "",
  );
  const [callbackBearerToken, setCallbackBearerToken] = useState("");
  const [callbackTimeoutMs, setCallbackTimeoutMs] = useState(initial?.callbackTimeoutMs ?? 10000);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const body: CallbackConfigRequest = {
        callbackBaseUrl: callbackBaseUrl || null,
        callbackAuthScheme: callbackAuthScheme || null,
        callbackBearerToken: callbackBearerToken || null,
        callbackTimeoutMs,
      };
      await setCallback(systemKey, body);
      onSaved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ModalShell title="Configure outbound callback" onClose={onClose}>
      <p className="mb-3 text-[11.5px] text-[var(--color-ink-500)]">
        DTMS POSTs callback events to <code>&#123;baseUrl&#125;/events</code>. Bearer token is
        stored once + reused; leave token blank to keep current value.
      </p>
      <div className="space-y-3">
        <Field label="Callback base URL">
          <input
            value={callbackBaseUrl}
            onChange={(e) => setCallbackBaseUrl(e.target.value)}
            placeholder="https://oms.internal/dtms-callbacks"
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[12px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
        <Field label="Auth scheme">
          <select
            value={callbackAuthScheme}
            onChange={(e) => setCallbackAuthScheme(e.target.value)}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          >
            <option value="">— none —</option>
            <option value="bearer">Bearer token</option>
          </select>
        </Field>
        {callbackAuthScheme === "bearer" && (
          <Field label="Bearer token (sent on every callback)">
            <input
              value={callbackBearerToken}
              onChange={(e) => setCallbackBearerToken(e.target.value)}
              placeholder="Leave blank to keep current"
              className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[12px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
              type="password"
            />
          </Field>
        )}
        <Field label="Timeout (ms)">
          <input
            type="number"
            value={callbackTimeoutMs}
            onChange={(e) => setCallbackTimeoutMs(parseInt(e.target.value || "0", 10))}
            min={1000}
            max={60000}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
      </div>
      {error && <ErrorBanner message={error} />}
      <ModalFooter onCancel={onClose} onSave={submit} saving={submitting} />
    </ModalShell>
  );
}

function ModalShell({ title, children, onClose }: { title: string; children: React.ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl border border-[var(--color-ink-100)] bg-white p-5 shadow-xl dark:border-white/[0.06] dark:bg-[var(--color-ink-900)]">
        <div className="flex items-start justify-between">
          <h2 className="text-base font-semibold text-[var(--color-ink-900)] dark:text-white">{title}</h2>
          <button type="button" onClick={onClose} className="rounded p-1 text-[var(--color-ink-400)] hover:bg-[var(--color-ink-50)]">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="mt-4">{children}</div>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
        {label}
      </label>
      <div className="mt-1">{children}</div>
    </div>
  );
}

function ErrorBanner({ message }: { message: string }) {
  return (
    <div className="mt-3 flex items-start gap-2 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700">
      <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
      <span>{message}</span>
    </div>
  );
}

function IssueTokenModal({
  systemKey,
  submitting,
  onCancel,
  onIssue,
}: {
  systemKey: string;
  submitting: boolean;
  onCancel: () => void;
  onIssue: (lifetimeSeconds: number) => void;
}) {
  // 90 days default — balances "partner doesn't have to come back too often"
  // against "stops working before it's truly forgotten." Operator can pick
  // shorter for higher-stakes integrations.
  const [days, setDays] = useState(90);
  const lifetimeSeconds = days * 86400;
  // Mirror endpoint validation so the operator sees the cap before they hit
  // submit — bad UX to bounce off the backend with a 400.
  const valid = days >= 1 && days <= 365;

  return (
    <ModalShell title={`Issue JWT for "${systemKey}"`} onClose={onCancel}>
      <p className="text-[12px] text-[var(--color-ink-500)]">
        Generates a JWT signed by DTMS that the partner sends directly as
        {" "}<code>Authorization: Bearer &lt;token&gt;</code>{" "}— no OAuth
        round-trip on their side. Pick a lifetime; pass the token to the
        partner via a secure channel.
      </p>
      <div className="mt-4 space-y-3">
        <Field label="Lifetime (days)">
          <div className="flex flex-wrap gap-1.5">
            {[7, 30, 90, 180, 365].map((d) => (
              <button
                key={d}
                type="button"
                onClick={() => setDays(d)}
                className={cn(
                  "rounded border px-2.5 py-1 text-[12px]",
                  days === d
                    ? "border-sky-400 bg-sky-50 text-sky-700"
                    : "border-[var(--color-ink-200)] bg-white text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]",
                )}
              >
                {d} {d === 1 ? "day" : "days"}
              </button>
            ))}
          </div>
          <div className="mt-2 flex items-center gap-2">
            <input
              type="number"
              min={1}
              max={365}
              value={days}
              onChange={(e) => setDays(Number(e.target.value) || 0)}
              className="w-20 rounded border border-[var(--color-ink-200)] bg-white px-2 py-1 text-[13px]"
            />
            <span className="text-[11px] text-[var(--color-ink-500)]">days (1-365)</span>
          </div>
        </Field>
        <div className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-[11px] text-amber-800">
          ⚠ Token will be valid for <strong>{days} days</strong>. If leaked,
          it's accepted until that date (no per-token revocation in V1).
          Deactivating the system or rotating the signing keypair is the
          only kill switch.
        </div>
      </div>
      <div className="mt-5 flex items-center justify-end gap-2">
        <button
          type="button"
          onClick={onCancel}
          className="rounded border border-[var(--color-ink-200)] bg-white px-3 py-1.5 text-[12px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
        >
          Cancel
        </button>
        <button
          type="button"
          disabled={!valid || submitting}
          onClick={() => onIssue(lifetimeSeconds)}
          className="rounded bg-sky-600 px-3 py-1.5 text-[12px] font-semibold text-white hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? "Issuing…" : "Issue token"}
        </button>
      </div>
    </ModalShell>
  );
}

function ModalFooter({ onCancel, onSave, saving }: { onCancel: () => void; onSave: () => void; saving: boolean }) {
  return (
    <div className="mt-5 flex items-center justify-end gap-2">
      <button
        type="button"
        onClick={onCancel}
        className="rounded border border-[var(--color-ink-200)] bg-white px-3 py-1.5 text-[12px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
      >
        Cancel
      </button>
      <button
        type="button"
        disabled={saving}
        onClick={onSave}
        className="rounded bg-[var(--color-ink-900)] px-3 py-1.5 text-[12px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:opacity-50"
      >
        {saving ? "Saving…" : "Save"}
      </button>
    </div>
  );
}
