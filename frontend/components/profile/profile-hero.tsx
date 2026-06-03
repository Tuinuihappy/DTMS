"use client";

import { BadgeCheck, MapPin, Pencil, Share2, ShieldCheck } from "lucide-react";
import { motion } from "motion/react";
import { useAuth } from "@/components/auth/auth-provider";
import { useProfileData } from "@/components/profile/profile-context";
import { Avatar } from "@/components/primitives/avatar";
import { StatusPulse } from "@/components/primitives/status-pulse";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

export function ProfileHero() {
  const { user } = useAuth();
  const profile = useProfileData();
  const live = profile.status === "ready" ? profile.data : null;

  // Prefer live API data; fall back to auth-context (JWT-derived) so the
  // hero renders instantly while the network call resolves.
  const displayName = live?.displayName || user?.displayName || "Operator";
  const employeeCode = live?.employeeId || user?.employeeCode || "—";
  const role = (live?.roles?.[0] || user?.role || "OPERATOR").toUpperCase();
  const photo = live?.thumbnailPhoto || user?.thumbnailPhoto;
  const photoSrc = photo
    ? photo.startsWith("data:")
      ? photo
      : `data:image/jpeg;base64,${photo}`
    : null;

  return (
    <motion.section
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.65, ease: EASE }}
      className="relative overflow-hidden rounded-[var(--radius-2xl)] mt-2"
    >
      <HeroBackdrop />

      <div className="relative px-6 pt-10 pb-8 sm:px-10 sm:pt-14 sm:pb-10 md:px-14 md:pt-16 md:pb-12">
        <div className="flex flex-col gap-8 md:flex-row md:items-end md:justify-between">
          {/* Identity block */}
          <div className="flex flex-col gap-5 sm:flex-row sm:items-end">
            <motion.div
              initial={{ opacity: 0, scale: 0.92, y: 8 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              transition={{ duration: 0.7, delay: 0.15, ease: EASE }}
              className="relative"
            >
              <div className="relative rounded-full p-[3px] bg-gradient-to-br from-white/95 via-white/70 to-white/30 shadow-[0_18px_42px_-18px_rgba(15,23,42,0.5)] dark:from-white/40 dark:via-white/15 dark:to-white/5">
                {photoSrc ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img
                    src={photoSrc}
                    alt={displayName}
                    className="h-[92px] w-[92px] rounded-full object-cover sm:h-[104px] sm:w-[104px]"
                  />
                ) : (
                  <Avatar
                    name={displayName}
                    hue={26}
                    size="lg"
                    className="!h-[92px] !w-[92px] !text-[26px] sm:!h-[104px] sm:!w-[104px]"
                  />
                )}
              </div>
              {/* Verified mark */}
              <span className="absolute -bottom-1 -right-1 grid h-8 w-8 place-items-center rounded-full bg-white text-[var(--color-brand-500)] shadow-[0_6px_16px_-6px_rgba(79,93,255,0.6)] ring-2 ring-white dark:bg-[var(--color-surface)] dark:ring-[var(--color-surface)]">
                <BadgeCheck className="h-4.5 w-4.5" strokeWidth={2.4} />
              </span>
              {/* Online pulse */}
              <span className="absolute -top-0.5 right-1 inline-flex">
                <StatusPulse tone="success" size="md" />
              </span>
            </motion.div>

            <div className="min-w-0">
              <motion.div
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.55, delay: 0.25, ease: EASE }}
                className="inline-flex items-center gap-2 rounded-full border border-white/40 bg-white/30 px-3 py-1 text-[10.5px] font-semibold uppercase tracking-[0.18em] text-[var(--color-ink-700)] backdrop-blur-md dark:border-white/10 dark:bg-white/[0.08] dark:text-[var(--color-ink-700)]"
              >
                <ShieldCheck className="h-3 w-3 text-[var(--color-brand-500)]" strokeWidth={2.4} />
                {role}
              </motion.div>

              <motion.h1
                initial={{ opacity: 0, y: 14 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.65, delay: 0.32, ease: EASE }}
                className="mt-3 font-display text-[2.2rem] leading-[1.05] font-semibold tracking-[-0.035em] text-[var(--color-ink-900)] sm:text-[2.65rem] md:text-[3rem]"
              >
                {displayName}
              </motion.h1>

              <motion.div
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.55, delay: 0.42, ease: EASE }}
                className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2 text-[13px] text-[var(--color-ink-600)]"
              >
                <span className="inline-flex items-center gap-1.5 font-mono text-[12.5px] tracking-tight text-[var(--color-ink-700)]">
                  <span className="text-[var(--color-ink-400)]">ID</span>
                  {employeeCode}
                </span>
                <span aria-hidden className="h-3 w-px bg-[var(--color-ink-200)]" />
                <span className="inline-flex items-center gap-1.5">
                  <MapPin className="h-3.5 w-3.5 text-[var(--color-coral)]" strokeWidth={2.2} />
                  Eastern Seaboard · Sector 04
                </span>
                <span aria-hidden className="h-3 w-px bg-[var(--color-ink-200)]" />
                <span className="inline-flex items-center gap-1.5">
                  <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-[var(--color-success)]">
                    <span className="absolute inset-0 rounded-full bg-[var(--color-success)] animate-ping opacity-60" />
                  </span>
                  On shift · clocked in 06:12
                </span>
              </motion.div>
            </div>
          </div>

          {/* Actions */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.55, delay: 0.5, ease: EASE }}
            className="flex flex-wrap items-center gap-2.5"
          >
            <button className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-200)]/70 bg-white/70 px-4 py-2.5 text-[13px] font-medium text-[var(--color-ink-700)] backdrop-blur-md transition-all hover:-translate-y-px hover:bg-white cursor-pointer dark:border-white/10 dark:bg-white/[0.06] dark:hover:bg-white/[0.12]">
              <Share2 className="h-3.5 w-3.5" strokeWidth={2} />
              Share profile
            </button>
            <button className="group inline-flex items-center gap-2 rounded-full bg-[var(--color-brand-900)] pl-4 pr-2 py-2 text-[13px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_14px_28px_-12px_rgba(14,21,48,0.55)] transition-transform duration-200 hover:-translate-y-0.5 cursor-pointer">
              <Pencil className="h-3.5 w-3.5" strokeWidth={2.4} />
              Edit profile
              <span className="ml-1 grid h-7 w-7 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-12">
                <Pencil className="h-3.5 w-3.5" strokeWidth={2.6} />
              </span>
            </button>
          </motion.div>
        </div>
      </div>
    </motion.section>
  );
}

/* -------------------------------------------------------------------------- */
/* Cinematic logistics backdrop                                               */
/* Aurora gradient mesh + abstract route topology that draws itself in once   */
/* on mount. Honors prefers-reduced-motion via globals.css's animation kill.  */
/* -------------------------------------------------------------------------- */
function HeroBackdrop() {
  return (
    <div className="absolute inset-0 -z-0 overflow-hidden rounded-[var(--radius-2xl)]">
      {/* Base coral->brand aurora */}
      <div
        className="absolute inset-0"
        style={{
          background:
            "linear-gradient(135deg, #ffe9e2 0%, #fcd9d1 20%, #f3d2e6 45%, #d6dcff 75%, #c7ccff 100%)",
        }}
      />
      <div
        className="absolute inset-0 hidden dark:block"
        style={{
          background:
            "linear-gradient(135deg, #1a1235 0%, #251a3f 25%, #1f1a40 55%, #161c45 85%, #0d1431 100%)",
        }}
      />

      {/* Aurora blooms */}
      <div
        aria-hidden
        className="pointer-events-none absolute -top-32 -left-20 h-[480px] w-[480px] rounded-full opacity-80 blur-[80px]"
        style={{
          background:
            "radial-gradient(circle at 40% 40%, rgba(255,138,120,0.65), transparent 65%)",
          animation: "float-y 8s ease-in-out infinite",
        }}
      />
      <div
        aria-hidden
        className="pointer-events-none absolute -bottom-40 -right-20 h-[520px] w-[520px] rounded-full opacity-70 blur-[90px]"
        style={{
          background:
            "radial-gradient(circle at 60% 60%, rgba(143,156,255,0.6), transparent 65%)",
          animation: "float-y 10s ease-in-out infinite",
          animationDelay: "-3s",
        }}
      />
      <div
        aria-hidden
        className="pointer-events-none absolute top-1/3 left-1/2 h-[360px] w-[360px] -translate-x-1/2 rounded-full opacity-60 blur-[70px]"
        style={{
          background:
            "radial-gradient(circle at 50% 50%, rgba(255,180,140,0.55), transparent 70%)",
          animation: "float-y 9s ease-in-out infinite",
          animationDelay: "-5s",
        }}
      />

      {/* Route topology — animated SVG paths */}
      <svg
        className="absolute inset-0 h-full w-full"
        viewBox="0 0 1200 360"
        preserveAspectRatio="none"
        aria-hidden
      >
        <defs>
          <linearGradient id="route-line" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="rgba(255,107,91,0)" />
            <stop offset="40%" stopColor="rgba(255,107,91,0.45)" />
            <stop offset="100%" stopColor="rgba(79,93,255,0.55)" />
          </linearGradient>
          <linearGradient id="route-line-dark" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="rgba(255,123,109,0)" />
            <stop offset="40%" stopColor="rgba(255,123,109,0.55)" />
            <stop offset="100%" stopColor="rgba(139,149,255,0.7)" />
          </linearGradient>
        </defs>

        {/* Light routes */}
        <g className="dark:hidden">
          <motion.path
            d="M -20 280 Q 200 220 380 240 T 720 180 T 1220 90"
            stroke="url(#route-line)"
            strokeWidth={1.4}
            fill="none"
            initial={{ pathLength: 0, opacity: 0 }}
            animate={{ pathLength: 1, opacity: 1 }}
            transition={{ duration: 1.8, ease: EASE, delay: 0.1 }}
          />
          <motion.path
            d="M -20 60 Q 240 130 460 100 T 820 200 T 1220 260"
            stroke="url(#route-line)"
            strokeWidth={1}
            fill="none"
            initial={{ pathLength: 0, opacity: 0 }}
            animate={{ pathLength: 1, opacity: 0.7 }}
            transition={{ duration: 2.1, ease: EASE, delay: 0.3 }}
          />
          <motion.path
            d="M -20 180 Q 260 230 520 200 T 980 140 T 1220 170"
            stroke="rgba(15,23,42,0.06)"
            strokeWidth={0.8}
            strokeDasharray="3 6"
            fill="none"
            initial={{ pathLength: 0 }}
            animate={{ pathLength: 1 }}
            transition={{ duration: 2.4, ease: EASE, delay: 0.5 }}
          />
        </g>

        {/* Dark routes */}
        <g className="hidden dark:block">
          <motion.path
            d="M -20 280 Q 200 220 380 240 T 720 180 T 1220 90"
            stroke="url(#route-line-dark)"
            strokeWidth={1.6}
            fill="none"
            initial={{ pathLength: 0, opacity: 0 }}
            animate={{ pathLength: 1, opacity: 1 }}
            transition={{ duration: 1.8, ease: EASE, delay: 0.1 }}
          />
          <motion.path
            d="M -20 60 Q 240 130 460 100 T 820 200 T 1220 260"
            stroke="url(#route-line-dark)"
            strokeWidth={1.1}
            fill="none"
            initial={{ pathLength: 0, opacity: 0 }}
            animate={{ pathLength: 1, opacity: 0.75 }}
            transition={{ duration: 2.1, ease: EASE, delay: 0.3 }}
          />
          <motion.path
            d="M -20 180 Q 260 230 520 200 T 980 140 T 1220 170"
            stroke="rgba(255,255,255,0.08)"
            strokeWidth={0.9}
            strokeDasharray="3 6"
            fill="none"
            initial={{ pathLength: 0 }}
            animate={{ pathLength: 1 }}
            transition={{ duration: 2.4, ease: EASE, delay: 0.5 }}
          />
        </g>

        {/* Route nodes */}
        {[
          { cx: 200, cy: 232, r: 3 },
          { cx: 520, cy: 215, r: 2.5 },
          { cx: 820, cy: 165, r: 3.5 },
          { cx: 1080, cy: 110, r: 2.8 },
        ].map((n, i) => (
          <motion.circle
            key={i}
            cx={n.cx}
            cy={n.cy}
            r={n.r}
            className="fill-[var(--color-coral)] dark:fill-[var(--color-coral)]"
            initial={{ opacity: 0, scale: 0 }}
            animate={{ opacity: 1, scale: 1 }}
            transition={{ duration: 0.4, delay: 1.2 + i * 0.12, ease: EASE }}
          />
        ))}
      </svg>

      {/* Grain + edge vignette for refinement */}
      <div
        aria-hidden
        className="absolute inset-0 opacity-[0.22] mix-blend-overlay"
        style={{
          backgroundImage:
            "url(\"data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 0.06 0'/></filter><rect width='100%' height='100%' filter='url(%23n)'/></svg>\")",
        }}
      />
      <div
        aria-hidden
        className="absolute inset-0"
        style={{
          background:
            "radial-gradient(ellipse 120% 80% at 50% 0%, transparent 60%, rgba(15,23,42,0.08) 100%)",
        }}
      />

      {/* Inner glass edge */}
      <div
        aria-hidden
        className="absolute inset-0 rounded-[var(--radius-2xl)] pointer-events-none"
        style={{
          boxShadow:
            "inset 0 1.5px 0 rgba(255,255,255,0.7), inset 0 0 0 1px rgba(255,255,255,0.25), inset 0 -1px 0 rgba(15,23,42,0.06)",
        }}
      />
    </div>
  );
}
