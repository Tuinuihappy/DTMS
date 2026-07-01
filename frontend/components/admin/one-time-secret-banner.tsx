"use client";

import { useState } from "react";
import { AlertCircle, Check, CheckCircle2, Copy, Eye, EyeOff, ShieldCheck, X } from "lucide-react";
import { testCredential, type TestCredentialMode } from "@/lib/api/iam-systems";

/**
 * Phase S.6 — banner that shows a freshly minted plaintext secret ONCE
 * (on create-system + rotate-credential responses). The backend never
 * returns this value again, so the UI must communicate that clearly:
 *
 * - Plaintext is masked by default; reveal with the Eye toggle
 * - Copy button writes to clipboard with a 2s "Copied!" confirmation
 * - "Test key" button verifies the new key against the backend's
 *   /source/{key}/whoami probe in one click — gives instant feedback
 *   so the operator doesn't have to context-switch to Swagger
 * - Dismissable; once dismissed the plaintext is gone from React state
 * - Warning text ("Save this now — won't show again") sits above the box
 *
 * Mount in the same place where the success would normally show. The
 * parent owns the visible/hidden state — pass `onDismiss` to remove the
 * banner from your local state when the user closes it.
 */
export function OneTimeSecretBanner({
  title,
  secret,
  helpText,
  testKeyForSystem,
  testMode = "client-secret",
  onDismiss,
}: {
  title: string;
  secret: string;
  helpText?: string;
  // When set, surfaces a "Test this credential" button that calls
  // /api/admin/iam/systems/{testKeyForSystem}/test-key. Pass the
  // system slug the secret was minted for.
  testKeyForSystem?: string;
  // "client-secret" (default) → the route handler runs the full OAuth
  // exchange before calling /whoami. "jwt" → the secret IS the JWT;
  // the handler sends it as `Authorization: Bearer ...` directly.
  testMode?: TestCredentialMode;
  onDismiss: () => void;
}) {
  const [revealed, setRevealed] = useState(false);
  const [copied, setCopied] = useState(false);
  const [copiedHeader, setCopiedHeader] = useState(false);
  const [testState, setTestState] = useState<
    | { kind: "idle" }
    | { kind: "testing" }
    | { kind: "ok"; message: string }
    | { kind: "fail"; message: string }
  >({ kind: "idle" });

  const masked = secret.length <= 12 ? "•".repeat(secret.length) : secret.slice(0, 8) + "•".repeat(20) + secret.slice(-4);

  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard not available — user can still reveal + select */
    }
  };

  // For JWT mode the partner pastes it as `Authorization: Bearer <jwt>` —
  // surface a one-click copy in that exact format so operators don't have
  // to remember to prepend "Bearer " (a recurring 401-in-Swagger
  // foot-gun: Swagger sends the Authorize value verbatim, no auto-prefix).
  const onCopyAsHeader = async () => {
    try {
      await navigator.clipboard.writeText(`Bearer ${secret}`);
      setCopiedHeader(true);
      setTimeout(() => setCopiedHeader(false), 2000);
    } catch { /* same fallback as onCopy */ }
  };

  const onTest = async () => {
    if (!testKeyForSystem) return;
    setTestState({ kind: "testing" });
    try {
      const result = await testCredential(testKeyForSystem, secret, testMode);
      if (result.ok) {
        setTestState({
          kind: "ok",
          message: `Authenticated as ${result.principalId ?? testKeyForSystem}${
            result.permissions ? ` · ${result.permissions.length} permissions` : ""
          }`,
        });
      } else {
        setTestState({
          kind: "fail",
          message: `Backend rejected the credential${result.status ? ` (HTTP ${result.status})` : ""}${
            result.message ? `: ${result.message}` : ""
          }`,
        });
      }
    } catch (e) {
      setTestState({ kind: "fail", message: (e as Error).message });
    }
  };

  return (
    <div className="rounded-lg border border-amber-400/60 bg-amber-50 px-4 py-4 dark:border-amber-500/40 dark:bg-amber-950/30">
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h4 className="text-sm font-semibold text-amber-900 dark:text-amber-200">{title}</h4>
          </div>
          <p className="mt-1 text-xs text-amber-800 dark:text-amber-300">
            Save this value now — it will not be shown again. Storing it
            anywhere else is the only way to recover it.
          </p>
          <p className="mt-1.5 text-[11.5px] font-semibold text-amber-900 dark:text-amber-200">
            This key is permanent — use it forever. Do NOT rotate routinely.
          </p>
          <p className="mt-0.5 text-[11px] text-amber-700/90 dark:text-amber-400/90">
            Only click Rotate when (a) the key was leaked, (b) a person who
            held it left, or (c) a compliance policy requires it. Otherwise
            paste this value into your password manager + the source-system
            integration once, and use it indefinitely.
          </p>
          {helpText && (
            <p className="mt-1.5 text-[11px] text-amber-700/80 dark:text-amber-400/80">{helpText}</p>
          )}

          <div className="mt-3 flex items-stretch gap-2">
            <code
              className="flex-1 select-all break-all rounded border border-amber-300 bg-white px-3 py-2 font-mono text-xs text-amber-950 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-100"
              data-testid="otsb-secret"
            >
              {revealed ? secret : masked}
            </code>
            <button
              type="button"
              onClick={() => setRevealed((r) => !r)}
              className="rounded border border-amber-300 bg-white px-3 text-amber-700 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-300 dark:hover:bg-amber-900"
              aria-label={revealed ? "Hide secret" : "Reveal secret"}
            >
              {revealed ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
            <button
              type="button"
              onClick={onCopy}
              className="rounded border border-amber-300 bg-white px-3 text-amber-700 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-300 dark:hover:bg-amber-900"
              aria-label="Copy secret to clipboard"
            >
              {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
            </button>
          </div>

          {testMode === "jwt" && (
            <div className="mt-2 flex items-center gap-2">
              <button
                type="button"
                onClick={onCopyAsHeader}
                className="inline-flex items-center gap-1 rounded border border-amber-300 bg-white px-2.5 py-1 text-[11px] font-medium text-amber-800 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-200 dark:hover:bg-amber-900"
              >
                {copiedHeader ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
                {copiedHeader ? "Copied!" : "Copy as `Bearer <jwt>`"}
              </button>
              <span className="text-[11px] text-amber-700/80 dark:text-amber-400/80">
                Use this when pasting into Swagger Authorize / curl <code>-H Authorization:</code>
              </span>
            </div>
          )}

          {testKeyForSystem && (
            <div className="mt-3 flex items-center gap-2">
              <button
                type="button"
                onClick={onTest}
                disabled={testState.kind === "testing"}
                className="inline-flex items-center gap-1 rounded border border-amber-300 bg-white px-2.5 py-1 text-[11px] font-medium text-amber-800 hover:bg-amber-100 disabled:opacity-50 dark:border-amber-500/40 dark:bg-amber-950 dark:text-amber-200 dark:hover:bg-amber-900"
              >
                <ShieldCheck className="h-3 w-3" strokeWidth={2.2} />
                {testState.kind === "testing" ? "Testing…" : "Test this credential"}
              </button>
              {testState.kind === "ok" && (
                <span className="inline-flex items-center gap-1 text-[11.5px] text-emerald-700 dark:text-emerald-300">
                  <CheckCircle2 className="h-3 w-3" strokeWidth={2.2} />
                  {testState.message}
                </span>
              )}
              {testState.kind === "fail" && (
                <span className="inline-flex items-center gap-1 text-[11.5px] text-rose-700 dark:text-rose-300">
                  <AlertCircle className="h-3 w-3" strokeWidth={2.2} />
                  {testState.message}
                </span>
              )}
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={onDismiss}
          className="rounded p-1 text-amber-700 hover:bg-amber-100 dark:text-amber-300 dark:hover:bg-amber-900"
          aria-label="Dismiss"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}
