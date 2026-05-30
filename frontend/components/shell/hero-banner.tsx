"use client";

import { Boxes, ChevronRight, Play } from "lucide-react";

import { Button } from "@/components/ui/button";
import type { OrderTemplateDto } from "@/types/order-template";

interface HeroBannerProps {
  latest: OrderTemplateDto | null;
  totalAction: number;
  totalOrder: number;
  onInstantiate: (t: OrderTemplateDto) => void;
  onCreateNewOrder: () => void;
}

// Adobe Stock-style hero — left half is a "what to do next" prompt,
// right half is a decorative pucks stack standing in for Adobe's 3D
// product illustration. Two modes:
//   - has a latest OrderTemplate → "Run again" CTA
//   - no templates yet → "Create your first one" CTA
export function HeroBanner({
  latest,
  totalAction,
  totalOrder,
  onInstantiate,
  onCreateNewOrder,
}: HeroBannerProps) {
  return (
    <section className="liquid-glass relative overflow-hidden rounded-[24px] p-6 md:p-7">
      <div className="relative z-[2] grid grid-cols-1 items-center gap-6 md:grid-cols-[1fr_auto]">
        <div className="space-y-3">
          <p className="text-[11px] font-semibold uppercase tracking-[0.08em] text-primary/90">
            {latest ? "Last instantiated" : "Get started"}
          </p>
          {latest ? (
            <>
              <h2 className="text-[22px] font-semibold tracking-tight leading-tight md:text-[26px]">
                {latest.name}
              </h2>
              <p className="max-w-xl text-[13px] text-muted-foreground">
                {latest.missions.length} mission
                {latest.missions.length === 1 ? "" : "s"} · priority{" "}
                {latest.priority}
                {latest.description ? ` · ${latest.description}` : ""}
              </p>
              <div className="pt-1">
                <Button
                  size="sm"
                  onClick={() => onInstantiate(latest)}
                  className="liquid-pill-primary rounded-full px-5 font-medium"
                >
                  <Play className="h-3.5 w-3.5 fill-current" strokeWidth={0} />
                  Run again
                </Button>
              </div>
            </>
          ) : (
            <>
              <h2 className="text-[22px] font-semibold tracking-tight leading-tight md:text-[26px]">
                Build your first OrderTemplate
              </h2>
              <p className="max-w-xl text-[13px] text-muted-foreground">
                Compose ActionTemplate recipes into a RIOT3 order plan
                and instantiate them at the press of a button.
              </p>
              <div className="pt-1">
                <Button
                  size="sm"
                  onClick={onCreateNewOrder}
                  className="liquid-pill-primary rounded-full px-5 font-medium"
                >
                  Create your first template
                  <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.25} />
                </Button>
              </div>
            </>
          )}
        </div>

        {/* Decorative right side — stack of pucks standing in for
            Adobe's 3D illustration. The pucks pick up the Sequoia
            gradient behind them. */}
        <div className="relative hidden h-32 w-48 shrink-0 items-center justify-end md:flex">
          <DecorativePucks />
          <div className="pointer-events-none absolute right-12 top-2 flex items-center gap-2 text-[11px] font-semibold text-muted-foreground">
            <span className="flex items-center gap-1.5">
              <Boxes className="h-3 w-3 text-primary" strokeWidth={2.25} />
              {totalAction}
            </span>
            <span className="text-muted-foreground/40">·</span>
            <span>{totalOrder} orders</span>
          </div>
        </div>
      </div>
    </section>
  );
}

function DecorativePucks() {
  return (
    <div className="relative h-32 w-32">
      {/* Big purple puck */}
      <div
        className="liquid-puck absolute right-0 top-4 h-20 w-20 rounded-full"
        style={
          {
            ["--tint" as string]:
              "color-mix(in oklch, oklch(0.70 0.18 290) 80%, white)",
          } as React.CSSProperties
        }
      />
      {/* Smaller teal puck */}
      <div
        className="liquid-puck absolute left-2 top-12 h-12 w-12 rounded-full"
        style={
          {
            ["--tint" as string]:
              "color-mix(in oklch, oklch(0.78 0.13 180) 80%, white)",
          } as React.CSSProperties
        }
      />
      {/* Tiny blue puck */}
      <div className="liquid-puck liquid-puck-primary absolute right-14 top-0 h-8 w-8 rounded-full" />
    </div>
  );
}
