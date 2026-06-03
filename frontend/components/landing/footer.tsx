"use client";

import { Github, Linkedin, Truck, Twitter, Youtube } from "lucide-react";

const cols = [
  {
    title: "Product",
    links: ["Features", "Platforms", "Integrations", "Changelog", "Roadmap"],
  },
  {
    title: "Company",
    links: ["About", "Customers", "Careers", "Press", "Partners"],
  },
  {
    title: "Resources",
    links: ["Docs", "API reference", "Status", "Community", "Help center"],
  },
  {
    title: "Legal",
    links: ["Terms", "Privacy", "Cookies", "DPA", "Security"],
  },
] as const;

export function LandingFooter() {
  return (
    <footer id="contact" className="mx-auto max-w-[1240px] px-4 pt-24 pb-12 sm:px-6 md:pt-32">
      <div className="glass glass-edge rounded-[var(--radius-2xl)] p-8 md:p-12">
        <div className="grid grid-cols-2 gap-10 md:grid-cols-6">
          {/* Brand block */}
          <div className="col-span-2 md:col-span-2">
            <div className="flex items-center gap-2.5">
              <span
                className="relative grid h-10 w-10 place-items-center rounded-[12px] text-[var(--color-ink-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.6),0_6px_14px_-4px_rgba(15,23,42,0.18)]"
                style={{
                  background:
                    "linear-gradient(135deg, #FFD8CC 0%, #F3D5EC 55%, #D7DBFF 100%)",
                }}
                aria-hidden
              >
                <Truck className="h-6 w-6" strokeWidth={1.75} />
              </span>
              <span className="font-display text-[1.15rem] font-semibold tracking-[0.04em] uppercase">
                TMS
              </span>
            </div>
            <p className="mt-4 max-w-xs text-[13.5px] leading-relaxed text-[var(--color-ink-500)]">
              Operations control deck for industrial logistics. Built in
              Bangkok. Trusted in 14 countries.
            </p>
            <div className="mt-5 flex items-center gap-2">
              {[
                { icon: <Twitter className="h-3.5 w-3.5" />, label: "Twitter" },
                { icon: <Linkedin className="h-3.5 w-3.5" />, label: "LinkedIn" },
                { icon: <Github className="h-3.5 w-3.5" />, label: "GitHub" },
                { icon: <Youtube className="h-3.5 w-3.5" />, label: "YouTube" },
              ].map((s) => (
                <a
                  key={s.label}
                  href="#"
                  aria-label={s.label}
                  className="grid h-9 w-9 place-items-center rounded-full bg-white/70 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all hover:-translate-y-px hover:bg-white hover:text-[var(--color-ink-900)] dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
                >
                  {s.icon}
                </a>
              ))}
            </div>
          </div>

          {/* Link columns */}
          {cols.map((c) => (
            <div key={c.title}>
              <div className="text-[10.5px] uppercase tracking-[0.14em] font-semibold text-[var(--color-ink-400)]">
                {c.title}
              </div>
              <ul className="mt-4 space-y-2.5">
                {c.links.map((l) => (
                  <li key={l}>
                    <a
                      href="#"
                      className="text-[13px] font-medium text-[var(--color-ink-700)] transition-colors hover:text-[var(--color-ink-900)]"
                    >
                      {l}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-10 inset-divider" />
        <div className="mt-5 flex flex-col gap-2 md:flex-row md:items-center md:justify-between text-[11.5px] text-[var(--color-ink-500)]">
          <div>
            © 2026 TMS Logistics Co., Ltd. All rights reserved.
          </div>
          <div className="flex items-center gap-1.5">
            <span className="inline-block h-1 w-1 rounded-full bg-[var(--color-success)]" />
            All systems operational ·{" "}
            <span className="font-mono">99.98%</span> 30-day uptime
          </div>
        </div>
      </div>
    </footer>
  );
}
