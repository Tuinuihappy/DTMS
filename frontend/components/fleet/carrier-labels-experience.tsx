"use client";

import { ArrowLeft, Download, Loader2, Printer, QrCode } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { PermissionGuard } from "@/components/auth/permission-guard";
import { TableEmptyState } from "@/components/primitives/data-table/table-empty-state";
import { GlassCard } from "@/components/primitives/glass-card";
import {
  getCarrierTypeProfiles,
  type CarrierTypeProfile,
} from "@/lib/api/fleet-carrier-types";
import { getCarriers, type Carrier, type CarrierStatus } from "@/lib/api/fleet-carriers";
import { Permissions } from "@/lib/auth/permissions";
import { carrierLabelSvg, carrierQrSvg, downloadSvg } from "@/lib/qr";
import { cn } from "@/lib/utils";

const STATUSES: CarrierStatus[] = ["Available", "InUse", "Maintenance", "Retired"];

// The registry list caps at 200 server-side (GetCarriersQuery.MaxPageSize). A
// sheet is something you print once per batch of new carriers, not a report, so
// one page is the whole feature — but say so when it truncates rather than
// silently printing a subset.
const SHEET_LIMIT = 200;

export function CarrierLabelsExperience() {
  return (
    <PermissionGuard requires={Permissions.Fleet.CarrierRead}>
      <Inner />
    </PermissionGuard>
  );
}

function Inner() {
  const [status, setStatus] = useState("");
  const [typeCode, setTypeCode] = useState("");

  const [rows, setRows] = useState<Carrier[]>([]);
  const [total, setTotal] = useState(0);
  const [types, setTypes] = useState<CarrierTypeProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      getCarriers({ status, carrierTypeCode: typeCode, pageSize: SHEET_LIMIT }, signal)
        .then((res) => {
          setRows(res.data);
          setTotal(res.totalCount);
          setLoading(false);
        })
        .catch((e: Error) => {
          if (e.name === "AbortError") return;
          setError(e.message);
          setLoading(false);
        });
    },
    [status, typeCode],
  );

  useEffect(() => {
    const ac = new AbortController();
    refresh(ac.signal);
    return () => ac.abort();
  }, [refresh]);

  useEffect(() => {
    let cancelled = false;
    getCarrierTypeProfiles()
      .then((t) => {
        if (!cancelled) setTypes(t);
      })
      .catch(() => {
        // A missing type list only costs the filter dropdown — the sheet itself
        // still renders, so this must not take the page down with it.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="space-y-5">
      {/* ── header + controls: excluded from print by the sheet-scoped rules ── */}
      <header className="carrier-label-chrome space-y-2">
        {/* Same back-link idiom as the admin detail pages. The rail has no
            entry for this page, so without it the only way back is the browser. */}
        <Link
          href="/fleet/carriers"
          className="inline-flex items-center gap-1 text-[11.5px] text-[var(--color-ink-500)] hover:text-[var(--color-ink-700)]"
        >
          <ArrowLeft className="h-3 w-3" strokeWidth={2.2} />
          All carriers
        </Link>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex items-center gap-3">
            <span className="inline-flex h-10 w-10 items-center justify-center rounded-2xl bg-gradient-to-br from-[var(--color-brand-500)] to-[var(--color-brand-600)] text-white">
              <QrCode className="h-5 w-5" strokeWidth={2.2} />
            </span>
            <div>
              <h1 className="font-display text-[22px] font-semibold text-[var(--color-ink-900)]">
                Carrier labels
              </h1>
              <p className="text-[12.5px] text-[var(--color-ink-500)]">
                Print QR stickers for the shelves themselves.
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => window.print()}
            disabled={rows.length === 0}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold uppercase tracking-[0.06em] transition-all",
              rows.length === 0
                ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
                : "bg-[var(--color-brand-500)] text-white hover:shadow-[0_14px_36px_-12px_rgba(59,130,246,0.5)]",
            )}
          >
            <Printer className="h-3.5 w-3.5" strokeWidth={2.4} />
            Print sheet
          </button>
        </div>
      </header>

      <GlassCard className="carrier-label-chrome flex flex-wrap items-center gap-2 px-4 py-3">
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          className={inputCls}
        >
          <option value="">All statuses</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>

        <select
          value={typeCode}
          onChange={(e) => setTypeCode(e.target.value)}
          className={inputCls}
        >
          <option value="">All types</option>
          {types.map((t) => (
            <option key={t.code} value={t.code}>
              {t.code}
            </option>
          ))}
        </select>

        <span className="ml-auto text-[11.5px] text-[var(--color-ink-500)]">
          {rows.length} label{rows.length === 1 ? "" : "s"}
          {total > rows.length && ` of ${total} — showing the first ${SHEET_LIMIT}`}
        </span>
      </GlassCard>

      {error ? (
        <GlassCard className="carrier-label-chrome px-4 py-6 text-[13px] text-[var(--color-coral)]">
          {error}
        </GlassCard>
      ) : loading ? (
        <GlassCard className="carrier-label-chrome flex items-center justify-center px-4 py-12">
          <Loader2 className="h-5 w-5 animate-spin text-[var(--color-ink-400)]" />
        </GlassCard>
      ) : rows.length === 0 ? (
        <TableEmptyState
          variant={status || typeCode ? "no-filter-match" : "no-data"}
          title="No carriers to label"
          body="Register a carrier, or widen the filters."
          icon={QrCode}
        />
      ) : (
        <div className="carrier-label-sheet grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {rows.map((c) => (
            <LabelCard key={c.id} carrier={c} />
          ))}
        </div>
      )}
    </div>
  );
}

function LabelCard({ carrier }: { carrier: Carrier }) {
  const [svg, setSvg] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    carrierQrSvg(carrier.carrierCode)
      .then((s) => {
        if (!cancelled) setSvg(s);
      })
      .catch(() => {
        // Leaves the placeholder in place. A card that cannot draw its code is
        // visibly blank rather than printing a sticker nobody can scan.
      });
    return () => {
      cancelled = true;
    };
  }, [carrier.carrierCode]);

  return (
    <div className="carrier-label-card group relative flex flex-col items-center gap-2 rounded-[var(--radius-lg)] border border-[var(--color-ink-100)] bg-white p-4 dark:border-white/[0.08] dark:bg-white">
      <div className="aspect-square w-full max-w-[180px] [&>svg]:h-full [&>svg]:w-full">
        {svg ? (
          // Generated by the qrcode library from a code the domain already
          // constrains to [A-Z0-9._-] — there is no caller-supplied markup here.
          <div dangerouslySetInnerHTML={{ __html: svg }} />
        ) : (
          <div className="h-full w-full animate-pulse rounded bg-[var(--color-ink-100)]" />
        )}
      </div>

      {/* The whole fallback when a sticker is scraped unreadable: a human can
          still type this. Never drop it to save space. */}
      <div className="text-center font-mono text-[13px] font-semibold tracking-tight text-black">
        {carrier.carrierCode}
      </div>
      {carrier.displayName && (
        <div className="-mt-1 text-center text-[10.5px] text-neutral-600">
          {carrier.displayName}
        </div>
      )}

      <button
        type="button"
        disabled={!svg}
        onClick={() =>
          svg &&
          downloadSvg(
            `${carrier.carrierCode}.svg`,
            // The whole card, not just the QR — a bare square with no code under
            // it is no use once it has been printed and stuck to a shelf.
            carrierLabelSvg(svg, carrier.carrierCode, carrier.displayName),
          )
        }
        title="Download SVG"
        aria-label={`Download label for ${carrier.carrierCode}`}
        className="carrier-label-chrome absolute right-2 top-2 rounded-full bg-[var(--color-ink-100)] p-1.5 text-[var(--color-ink-600)] opacity-0 transition-opacity hover:bg-[var(--color-ink-200)] focus-visible:opacity-100 group-hover:opacity-100 disabled:cursor-not-allowed"
      >
        <Download className="h-3.5 w-3.5" strokeWidth={2.4} />
      </button>
    </div>
  );
}

const inputCls =
  "rounded-full border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-1.5 text-[12px] text-[var(--color-ink-800)] focus:border-[var(--color-brand-500)] focus:outline-none";
