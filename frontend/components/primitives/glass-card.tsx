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
  "pastel-sky": "bg-[var(--color-pastel-sky)] border border-white/60 dark:border-white/[0.08]",
  "pastel-lavender": "bg-[var(--color-pastel-lavender)] border border-white/60 dark:border-white/[0.08]",
  "pastel-peach": "bg-[var(--color-pastel-peach)] border border-white/60 dark:border-white/[0.08]",
  "pastel-mint": "bg-[var(--color-pastel-mint)] border border-white/60 dark:border-white/[0.08]",
  // Comms / dark-surface variant. Light: deep navy (#0E1530 token). Dark:
  // raised navy surface so it differentiates from the canvas without
  // flipping to the electric brand-blue.
  ink: "bg-[#0E1530] dark:bg-[var(--color-surface-soft)] text-white border border-white/[0.08]",
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
