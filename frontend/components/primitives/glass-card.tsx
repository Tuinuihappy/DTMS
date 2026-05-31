"use client";

import { motion, type MotionProps } from "motion/react";
import { forwardRef, type HTMLAttributes } from "react";
import { cn } from "@/lib/utils";

type GlassCardProps = HTMLAttributes<HTMLDivElement> &
  MotionProps & {
    variant?: "default" | "strong" | "pastel-sky" | "pastel-lavender" | "pastel-peach" | "pastel-mint" | "ink";
    interactive?: boolean;
  };

const variantClass: Record<NonNullable<GlassCardProps["variant"]>, string> = {
  default: "glass",
  strong: "glass-strong",
  "pastel-sky": "bg-[var(--color-pastel-sky)] border border-white/60",
  "pastel-lavender": "bg-[var(--color-pastel-lavender)] border border-white/60",
  "pastel-peach": "bg-[var(--color-pastel-peach)] border border-white/60",
  "pastel-mint": "bg-[var(--color-pastel-mint)] border border-white/60",
  ink: "bg-[var(--color-brand-900)] text-white border border-white/8",
};

export const GlassCard = forwardRef<HTMLDivElement, GlassCardProps>(
  ({ className, variant = "default", interactive = false, children, ...props }, ref) => {
    return (
      <motion.div
        ref={ref}
        className={cn(
          "relative rounded-[var(--radius-lg)] overflow-hidden",
          variantClass[variant],
          interactive && "cursor-pointer transition-shadow duration-300 hover:shadow-[0_30px_60px_-24px_rgba(15,23,42,0.18)]",
          className,
        )}
        {...props}
      >
        {children}
      </motion.div>
    );
  },
);
GlassCard.displayName = "GlassCard";
