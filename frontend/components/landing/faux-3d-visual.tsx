"use client";

import { motion } from "motion/react";

/**
 * Faux-3D hero piece — no Three.js / Spline. Just CSS + Motion:
 *   - A huge mesh-gradient backdrop (4 radial gradients on a rotating
 *     wrapper) for the "iridescent atmosphere".
 *   - Conic-gradient iridescent rings that drift + rotate.
 *   - Floating glass shards (rounded rectangles + spheres) with their
 *     own conic spectrums + heavy blur, drifting on independent loops.
 *   - One "hero shard" anchored centre-right with a refraction sheen.
 *
 * The whole thing reads as a chunky 3D object on a holographic dais
 * without paying the WebGL / runtime cost. Loops are 6-12 s and
 * `prefers-reduced-motion` halts them via the globals.css guard.
 */
export function FauxThreeDVisual() {
  return (
    <div className="relative aspect-[5/6] md:aspect-[4/5] w-full select-none [perspective:1200px]">
      {/* Mesh gradient atmosphere — rotates slowly behind everything */}
      <motion.div
        animate={{ rotate: 360 }}
        transition={{ duration: 60, repeat: Infinity, ease: "linear" }}
        className="absolute inset-[-15%] opacity-90"
        style={{
          background:
            "radial-gradient(circle at 25% 30%, #b8c5ff 0%, transparent 40%)," +
            "radial-gradient(circle at 80% 20%, #ffcdb2 0%, transparent 45%)," +
            "radial-gradient(circle at 70% 80%, #c7f0d8 0%, transparent 40%)," +
            "radial-gradient(circle at 20% 75%, #e5cfff 0%, transparent 45%)",
          filter: "blur(40px)",
        }}
        aria-hidden
      />

      {/* Soft white dais — a faint disc the shards "stand on" */}
      <div
        className="absolute left-1/2 -translate-x-1/2 bottom-[8%] h-[55%] w-[80%] rounded-full opacity-70"
        style={{
          background:
            "radial-gradient(ellipse at center, rgba(255,255,255,0.9) 0%, rgba(255,255,255,0.4) 30%, transparent 70%)",
          filter: "blur(10px)",
        }}
        aria-hidden
      />

      {/* Iridescent conic ring — main */}
      <FloatingShard
        className="absolute top-[12%] right-[8%] h-[42%] w-[42%]"
        rotateRange={[-8, 8]}
        yRange={[0, -14]}
        duration={9}
      >
        <div
          className="h-full w-full rounded-full"
          style={{
            background:
              "conic-gradient(from 220deg, #a78bfa, #f0abfc, #fca5a5, #fdba74, #fde68a, #86efac, #67e8f9, #a78bfa)",
            filter: "blur(0.5px)",
            boxShadow:
              "inset 0 4px 20px rgba(255,255,255,0.6), inset 0 -8px 30px rgba(15,23,42,0.15), 0 30px 60px -20px rgba(79,93,255,0.4)",
            transform: "rotateX(28deg) rotateZ(-15deg)",
          }}
        >
          <div
            className="absolute inset-[14%] rounded-full"
            style={{
              background:
                "radial-gradient(circle at 30% 25%, rgba(255,255,255,0.92), rgba(255,255,255,0.3) 40%, transparent 70%)",
            }}
          />
        </div>
      </FloatingShard>

      {/* Hero shard — chunky rounded square in centre */}
      <FloatingShard
        className="absolute top-[28%] left-[18%] h-[36%] w-[36%]"
        rotateRange={[-4, 4]}
        yRange={[0, -10]}
        duration={11}
      >
        <div
          className="relative h-full w-full overflow-hidden rounded-[40%]"
          style={{
            background:
              "conic-gradient(from 45deg at 50% 50%, #c7d4ff, #e0d4ff, #ffd4e6, #ffe7c7, #d4f0ff, #c7d4ff)",
            boxShadow:
              "inset 0 6px 20px rgba(255,255,255,0.85), inset 0 -10px 30px rgba(79,93,255,0.25), 0 20px 50px -10px rgba(79,93,255,0.4), 0 40px 80px -20px rgba(15,23,42,0.18)",
            transform: "rotateX(20deg) rotateY(-15deg) rotateZ(8deg)",
          }}
        >
          {/* Specular highlight */}
          <div
            className="absolute -top-[10%] left-[10%] h-[60%] w-[60%] rounded-full"
            style={{
              background:
                "radial-gradient(circle, rgba(255,255,255,0.85) 0%, rgba(255,255,255,0.2) 40%, transparent 70%)",
              filter: "blur(8px)",
            }}
          />
          {/* Bottom shadow ridge */}
          <div
            className="absolute -bottom-[20%] left-1/2 -translate-x-1/2 h-[40%] w-[80%] rounded-full"
            style={{
              background:
                "radial-gradient(ellipse, rgba(79,93,255,0.4) 0%, transparent 60%)",
              filter: "blur(15px)",
            }}
          />
        </div>
      </FloatingShard>

      {/* Small orbit shard — pill */}
      <FloatingShard
        className="absolute top-[55%] right-[24%] h-[20%] w-[16%]"
        rotateRange={[-12, 12]}
        yRange={[0, 12]}
        duration={7}
      >
        <div
          className="h-full w-full rounded-full"
          style={{
            background:
              "conic-gradient(from 0deg, #ff9ec7, #ffd3a5, #fff0a3, #a5ffd3, #a5d3ff, #ff9ec7)",
            boxShadow:
              "inset 0 3px 10px rgba(255,255,255,0.7), 0 12px 30px -8px rgba(255,158,199,0.5)",
            transform: "rotateX(30deg) rotateZ(20deg)",
          }}
        />
      </FloatingShard>

      {/* Small orbit shard — sphere */}
      <FloatingShard
        className="absolute top-[60%] left-[8%] h-[14%] w-[14%]"
        rotateRange={[-6, 6]}
        yRange={[0, -8]}
        duration={8}
      >
        <div
          className="h-full w-full rounded-full"
          style={{
            background:
              "conic-gradient(from 120deg, #fde68a, #fca5a5, #c4b5fd, #a5b4fc, #fde68a)",
            boxShadow:
              "inset 0 4px 12px rgba(255,255,255,0.7), 0 14px 30px -8px rgba(250,204,21,0.4)",
            transform: "rotateX(35deg)",
          }}
        >
          <div
            className="absolute top-[15%] left-[20%] h-[40%] w-[40%] rounded-full"
            style={{
              background:
                "radial-gradient(circle, rgba(255,255,255,0.9) 0%, transparent 60%)",
              filter: "blur(3px)",
            }}
          />
        </div>
      </FloatingShard>

      {/* Tiny accent — top-left */}
      <FloatingShard
        className="absolute top-[6%] left-[28%] h-[8%] w-[8%]"
        rotateRange={[-15, 15]}
        yRange={[-6, 6]}
        duration={6}
      >
        <div
          className="h-full w-full rounded-full"
          style={{
            background:
              "conic-gradient(from 90deg, #6f7bff, #c4b5fd, #fff, #fca5a5, #6f7bff)",
            boxShadow:
              "inset 0 2px 6px rgba(255,255,255,0.8), 0 8px 18px -4px rgba(111,123,255,0.5)",
          }}
        />
      </FloatingShard>

      {/* Decorative grid dots on the dais */}
      <svg
        className="absolute inset-x-0 bottom-0 h-[20%] w-full opacity-30 dark:opacity-15"
        viewBox="0 0 400 80"
        aria-hidden
      >
        <defs>
          <radialGradient id="dotGrad">
            <stop offset="0%" stopColor="currentColor" stopOpacity="0.4" />
            <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
          </radialGradient>
          <pattern id="dotPattern" x="0" y="0" width="14" height="14" patternUnits="userSpaceOnUse">
            <circle cx="2" cy="2" r="1" fill="currentColor" opacity="0.4" />
          </pattern>
        </defs>
        <rect width="400" height="80" fill="url(#dotPattern)" />
        <rect width="400" height="80" fill="url(#dotGrad)" />
      </svg>
    </div>
  );
}

function FloatingShard({
  children,
  className,
  yRange,
  rotateRange,
  duration,
}: {
  children: React.ReactNode;
  className?: string;
  yRange: [number, number];
  rotateRange: [number, number];
  duration: number;
}) {
  return (
    <motion.div
      animate={{
        y: yRange,
        rotate: rotateRange,
      }}
      transition={{
        y: { duration, repeat: Infinity, repeatType: "reverse", ease: "easeInOut" },
        rotate: {
          duration: duration * 1.3,
          repeat: Infinity,
          repeatType: "reverse",
          ease: "easeInOut",
        },
      }}
      className={className}
      style={{ transformStyle: "preserve-3d" }}
    >
      {children}
    </motion.div>
  );
}
