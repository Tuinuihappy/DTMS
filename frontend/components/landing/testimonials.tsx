"use client";

import { motion } from "motion/react";
import { Avatar } from "@/components/primitives/avatar";

const ease = [0.22, 1, 0.36, 1] as const;

type Testimonial = {
  quote: string;
  name: string;
  role: string;
  handle: string;
  hue: number;
};

const testimonials: Testimonial[] = [
  {
    quote:
      "Switched our 240-truck fleet to TMS in 6 weeks. Dispatcher load dropped 38%, on-time SLA went from 84% to 96%.",
    name: "Marisa Petchara",
    role: "Head of Ops, SCG Logistics",
    handle: "@marisa.p",
    hue: 280,
  },
  {
    quote: "The live comms widget alone paid for the year. Drivers actually answer now.",
    name: "Niran Sukhothai",
    role: "Senior Dispatcher",
    handle: "@niran.s",
    hue: 18,
  },
  {
    quote:
      "We were stitching three spreadsheets together. Now everything's one screen and I sleep at night.",
    name: "Kenji Otsuka",
    role: "Cold Chain Lead",
    handle: "@kenji.o",
    hue: 30,
  },
  {
    quote: "Plugged into our Geotab ELDs in 20 minutes. The integrations are no joke.",
    name: "Alisa Wong",
    role: "Port Operations",
    handle: "@alisa.w",
    hue: 220,
  },
  {
    quote:
      "First TMS my drivers actually opened. The mobile app feels like a consumer product.",
    name: "Suchart Riantong",
    role: "Long-haul lead",
    handle: "@suchart.r",
    hue: 12,
  },
  {
    quote:
      "Audit trail saved us during a DOT review. Every dispatch action timestamped and signed.",
    name: "Pavel Iliescu",
    role: "Compliance Manager",
    handle: "@pavel.i",
    hue: 200,
  },
];

export function Testimonials() {
  return (
    <section id="testimonials" className="relative mx-auto max-w-[1240px] px-4 pt-24 sm:px-6 md:pt-32">
      <motion.div
        initial={{ opacity: 0, y: 14 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-15%" }}
        transition={{ duration: 0.6, ease }}
        className="text-center"
      >
        <h2 className="font-display text-[2.25rem] md:text-[3.25rem] leading-[1.05] tracking-[-0.03em] font-semibold text-[var(--color-ink-900)]">
          Real talk{" "}
          <span className="text-[var(--color-ink-500)]">from real dispatchers.</span>
        </h2>
        <p className="mt-4 max-w-xl mx-auto text-[15px] leading-relaxed text-[var(--color-ink-500)]">
          14 countries, 50+ fleets, ~9,000 trucks running on TMS today. Here&apos;s
          what the people actually using it say.
        </p>
      </motion.div>

      <div className="relative mt-12 overflow-hidden">
        {/* Edge fade masks */}
        <div className="pointer-events-none absolute left-0 inset-y-0 w-24 z-10 bg-gradient-to-r from-[var(--color-canvas)] via-[var(--color-canvas)]/60 to-transparent" />
        <div className="pointer-events-none absolute right-0 inset-y-0 w-24 z-10 bg-gradient-to-l from-[var(--color-canvas)] via-[var(--color-canvas)]/60 to-transparent" />

        <Marquee speed={45} className="py-3">
          {testimonials.map((t) => (
            <TestimonialCard key={t.handle + "-a"} {...t} />
          ))}
        </Marquee>
        <Marquee speed={55} reverse className="py-3">
          {[...testimonials].reverse().map((t) => (
            <TestimonialCard key={t.handle + "-b"} {...t} />
          ))}
        </Marquee>
      </div>
    </section>
  );
}

/* -------------------------------------------------------------------------- */
function Marquee({
  children,
  speed,
  reverse = false,
  className,
}: {
  children: React.ReactNode;
  speed: number;
  reverse?: boolean;
  className?: string;
}) {
  return (
    <div className={"flex gap-4 " + (className ?? "")}>
      <motion.div
        animate={{ x: reverse ? ["-50%", "0%"] : ["0%", "-50%"] }}
        transition={{ duration: speed, repeat: Infinity, ease: "linear" }}
        className="flex shrink-0 gap-4"
      >
        {children}
        {children}
      </motion.div>
    </div>
  );
}

function TestimonialCard({ quote, name, role, handle, hue }: Testimonial) {
  return (
    <div className="glass glass-edge w-[320px] md:w-[360px] shrink-0 rounded-[28px] p-5">
      <p className="text-[13.5px] leading-relaxed text-[var(--color-ink-800)]">
        &ldquo;{quote}&rdquo;
      </p>
      <div className="mt-4 flex items-center gap-2.5">
        <Avatar name={name} hue={hue} size="sm" ring />
        <div className="min-w-0">
          <div className="text-[12.5px] font-semibold text-[var(--color-ink-900)] truncate">
            {name}
          </div>
          <div className="text-[10.5px] text-[var(--color-ink-500)] truncate font-mono">
            {handle} · {role}
          </div>
        </div>
      </div>
    </div>
  );
}
