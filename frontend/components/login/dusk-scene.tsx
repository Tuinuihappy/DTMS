"use client";

import { motion } from "motion/react";
import { useMemo } from "react";

/* -------------------------------------------------------------------------- */
/* DuskScene — the cinematic left panel.                                       */
/*                                                                            */
/* Entirely CSS + inline SVG. No stock photography. The scene is composed     */
/* of four atmospheric layers (back→front):                                    */
/*   1. Sky gradient                — vertical violet→magenta→coral.           */
/*   2. Sun + halo                  — two radial gradients in a rising orb.   */
/*   3. Drifting clouds + particles — soft elliptical SVGs, slow drift.       */
/*   4. Ground + highway + truck    — silhouette horizon with perspective.    */
/*                                                                            */
/* All scene-load animation timing is centred on T0 = page mount. The right   */
/* form panel layers its own choreography on the same clock so they read as   */
/* one continuous sunrise → cargo-leaves-the-bay sequence.                    */
/* -------------------------------------------------------------------------- */

const EASE_OUT_QUART = [0.22, 1, 0.36, 1] as const;

export function DuskScene() {
  // Stable particle field — seeded with deterministic positions so SSR/CSR
  // markup matches and the dust pattern doesn't re-roll every render.
  const particles = useMemo(
    () =>
      Array.from({ length: 22 }, (_, i) => {
        // Pseudo-random but deterministic from index — keeps SSR stable.
        const seed = (i * 9301 + 49297) % 233280;
        const r = seed / 233280;
        const r2 = ((i * 5749 + 25307) % 65521) / 65521;
        return {
          left: 5 + r * 90,
          top: 30 + r2 * 60,
          delay: r * 6,
          duration: 8 + r2 * 10,
          size: 1 + r2 * 1.8,
        };
      }),
    [],
  );

  return (
    <div
      className="dusk-scene absolute inset-0 overflow-hidden bg-[#0A0420]"
      aria-hidden
    >
      {/* ── 1. Sky gradient ──────────────────────────────────────────────── */}
      <motion.div
        className="absolute inset-0"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.9, ease: EASE_OUT_QUART }}
        style={{
          background:
            "linear-gradient(180deg, #0E0728 0%, #2A1248 22%, #5C1C6E 48%, #9B2452 70%, #D63B5B 86%, #FF6F5C 100%)",
        }}
      />

      {/* ── 2. Sun: halo + core, rising from below the horizon ──────────── */}
      <motion.div
        className="absolute"
        style={{ left: "62%", top: "62%", transform: "translate(-50%, -50%)" }}
        initial={{ y: 160, opacity: 0, scale: 0.65 }}
        animate={{ y: 0, opacity: 1, scale: 1 }}
        transition={{ duration: 1.4, delay: 0.25, ease: EASE_OUT_QUART }}
      >
        {/* Halo */}
        <div
          className="absolute -translate-x-1/2 -translate-y-1/2 rounded-full"
          style={{
            width: 720,
            height: 720,
            background:
              "radial-gradient(closest-side, rgba(255,180,120,0.55) 0%, rgba(255,120,90,0.35) 28%, rgba(220,60,90,0.18) 55%, transparent 72%)",
            filter: "blur(8px)",
          }}
        />
        {/* Core — breathing pulse on a 6s loop, kept very subtle */}
        <motion.div
          className="relative rounded-full"
          style={{
            width: 220,
            height: 220,
            background:
              "radial-gradient(circle at 45% 45%, #FFF3C9 0%, #FFD56B 28%, #FF9A4C 60%, #FF5E5C 90%)",
            boxShadow:
              "0 0 90px 24px rgba(255, 170, 100, 0.45), 0 0 180px 60px rgba(255, 90, 90, 0.25)",
          }}
          animate={{ scale: [1, 1.03, 1] }}
          transition={{ duration: 6, ease: "easeInOut", repeat: Infinity }}
        />
      </motion.div>

      {/* ── 3. Drifting cloud strata ─────────────────────────────────────── */}
      <CloudStratum
        delay={0.55}
        top="18%"
        opacity={0.42}
        width={520}
        from={-200}
        duration={48}
      />
      <CloudStratum
        delay={0.65}
        top="34%"
        opacity={0.32}
        width={420}
        from={-280}
        duration={62}
      />
      <CloudStratum
        delay={0.75}
        top="46%"
        opacity={0.24}
        width={360}
        from={-180}
        duration={80}
      />

      {/* ── 4. Particle dust — floats forever ────────────────────────────── */}
      {particles.map((p, i) => (
        <motion.span
          key={i}
          className="absolute rounded-full bg-white"
          style={{
            left: `${p.left}%`,
            top: `${p.top}%`,
            width: p.size,
            height: p.size,
            opacity: 0,
            filter: "blur(0.3px)",
          }}
          animate={{
            y: [0, -22, 0],
            x: [0, 6, 0],
            opacity: [0, 0.7, 0],
          }}
          transition={{
            duration: p.duration,
            delay: p.delay,
            ease: "easeInOut",
            repeat: Infinity,
          }}
        />
      ))}

      {/* ── 5. Ground silhouette + highway ───────────────────────────────── */}
      <div
        className="absolute inset-x-0 bottom-0 h-[36%]"
        style={{
          background:
            "linear-gradient(180deg, transparent 0%, rgba(10,4,32,0.35) 22%, rgba(10,4,32,0.85) 55%, #06031A 100%)",
        }}
      />

      {/* Highway perspective + lane markers */}
      <svg
        className="absolute inset-x-0 bottom-0 w-full"
        viewBox="0 0 1200 380"
        preserveAspectRatio="xMidYMax slice"
        style={{ height: "36%" }}
      >
        <defs>
          <linearGradient id="tarmac" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#1A0B2E" stopOpacity="0" />
            <stop offset="35%" stopColor="#1A0B2E" stopOpacity="0.4" />
            <stop offset="100%" stopColor="#06031A" stopOpacity="1" />
          </linearGradient>
          <linearGradient id="laneFade" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#FFD56B" stopOpacity="0" />
            <stop offset="40%" stopColor="#FFD56B" stopOpacity="0.35" />
            <stop offset="100%" stopColor="#FFE7A3" stopOpacity="0.95" />
          </linearGradient>
          <linearGradient id="horizonLine" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="#FF6F5C" stopOpacity="0" />
            <stop offset="50%" stopColor="#FFD56B" stopOpacity="0.85" />
            <stop offset="100%" stopColor="#FF6F5C" stopOpacity="0" />
          </linearGradient>
        </defs>

        {/* Tarmac fill */}
        <path d="M 0 380 L 1200 380 L 1200 60 L 0 60 Z" fill="url(#tarmac)" />

        {/* Glowing horizon line */}
        <line x1="0" y1="60" x2="1200" y2="60" stroke="url(#horizonLine)" strokeWidth="1.2" />

        {/* Lane markers — converging from horizon, animated forward */}
        <g opacity="0.85">
          {[0, 1, 2, 3, 4, 5].map((i) => {
            const t = i / 6;
            const y1 = 60 + t * 320;
            const y2 = y1 + 8 + t * 14;
            const widen = 1 + t * 6;
            return (
              <line
                key={i}
                x1={600 - widen * 4}
                y1={y1}
                x2={600 + widen * 4}
                y2={y2}
                stroke="url(#laneFade)"
                strokeWidth={0.6 + t * 1.2}
                strokeLinecap="round"
              >
                <animate
                  attributeName="opacity"
                  values="0;1;0"
                  dur="3.6s"
                  begin={`${i * 0.6}s`}
                  repeatCount="indefinite"
                />
              </line>
            );
          })}
        </g>
      </svg>

      {/* ── 6. Truck silhouette — glides in across the mid-horizon ──────── */}
      <motion.svg
        viewBox="0 0 200 80"
        className="absolute"
        style={{
          left: "8%",
          bottom: "30%",
          width: 180,
          filter: "drop-shadow(0 6px 14px rgba(0,0,0,0.6))",
        }}
        initial={{ x: -260, opacity: 0 }}
        animate={{ x: 0, opacity: 1 }}
        transition={{ duration: 2.4, delay: 0.85, ease: EASE_OUT_QUART }}
        aria-hidden
      >
        <defs>
          <linearGradient id="truckBody" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#1A0F2E" />
            <stop offset="100%" stopColor="#06031A" />
          </linearGradient>
          <linearGradient id="truckRim" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#FFD56B" stopOpacity="0.95" />
            <stop offset="100%" stopColor="#FF7A3D" stopOpacity="0.4" />
          </linearGradient>
        </defs>
        {/* Box trailer */}
        <rect x="40" y="20" width="110" height="42" rx="3" fill="url(#truckBody)" />
        {/* Rim highlight along the top of the trailer — picks up dusk light */}
        <rect x="40" y="20" width="110" height="2" fill="url(#truckRim)" />
        {/* Cab */}
        <path
          d="M 150 30 L 170 30 L 178 40 L 178 62 L 150 62 Z"
          fill="url(#truckBody)"
        />
        {/* Windshield catching sun */}
        <path d="M 152 32 L 168 32 L 174 41 L 152 41 Z" fill="#FF8A5C" opacity="0.55" />
        {/* Wheels */}
        <circle cx="62" cy="64" r="6" fill="#0A0420" stroke="#FFD56B" strokeWidth="0.4" />
        <circle cx="128" cy="64" r="6" fill="#0A0420" stroke="#FFD56B" strokeWidth="0.4" />
        <circle cx="166" cy="64" r="6" fill="#0A0420" stroke="#FFD56B" strokeWidth="0.4" />
        {/* Headlight beam — faint warm cone */}
        <path
          d="M 178 48 L 198 40 L 198 58 L 178 56 Z"
          fill="#FFD56B"
          opacity="0.18"
        />
      </motion.svg>

      {/* ── 7. Grain overlay — gives the whole scene photographic texture ─ */}
      <div className="dusk-grain absolute inset-0 pointer-events-none" />

      {/* ── 8. Right-edge soft fade into the form panel area ─────────────── */}
      <div
        className="absolute inset-y-0 right-0 w-[18%] pointer-events-none hidden lg:block"
        style={{
          background:
            "linear-gradient(90deg, transparent 0%, rgba(10,4,32,0.0) 30%, rgba(10,4,32,0.35) 100%)",
        }}
      />
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* CloudStratum — one drifting layer. Mount-time fade-in, then x-drifts        */
/* indefinitely on a slow loop. Three of these stack at different altitudes    */
/* and speeds so the parallax reads as deep.                                   */
/* -------------------------------------------------------------------------- */
function CloudStratum({
  delay,
  top,
  opacity,
  width,
  from,
  duration,
}: {
  delay: number;
  top: string;
  opacity: number;
  width: number;
  from: number;
  duration: number;
}) {
  return (
    <motion.div
      className="absolute pointer-events-none"
      style={{ top, left: 0, right: 0 }}
      initial={{ opacity: 0 }}
      animate={{ opacity }}
      transition={{ duration: 1.4, delay, ease: EASE_OUT_QUART }}
    >
      <motion.svg
        viewBox="0 0 1200 120"
        className="block"
        style={{ width: "120%" }}
        initial={{ x: from }}
        animate={{ x: from + 200 }}
        transition={{ duration, ease: "linear", repeat: Infinity, repeatType: "reverse" }}
      >
        <defs>
          <radialGradient id={`cloud-${width}`} cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#FFE0C8" stopOpacity="0.95" />
            <stop offset="60%" stopColor="#FF9A8B" stopOpacity="0.45" />
            <stop offset="100%" stopColor="#FF6A6A" stopOpacity="0" />
          </radialGradient>
        </defs>
        <ellipse cx="200" cy="60" rx={width / 6} ry="18" fill={`url(#cloud-${width})`} />
        <ellipse cx="540" cy="42" rx={width / 5} ry="22" fill={`url(#cloud-${width})`} />
        <ellipse cx="940" cy="68" rx={width / 7} ry="14" fill={`url(#cloud-${width})`} />
      </motion.svg>
    </motion.div>
  );
}
