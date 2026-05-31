import { cn } from "@/lib/utils";
import type { ReactNode } from "react";

export function SectionLabel({
  icon,
  title,
  subtitle,
  action,
  className,
}: {
  icon?: ReactNode;
  title: string;
  subtitle?: string;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("flex items-start justify-between gap-4", className)}>
      <div className="flex items-start gap-3">
        {icon && (
          <span className="grid h-10 w-10 place-items-center rounded-[14px] bg-white/70 text-[var(--color-ink-700)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_10px_-4px_rgba(15,23,42,0.12)] dark:bg-white/[0.06] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_4px_10px_-4px_rgba(0,0,0,0.5)]">
            {icon}
          </span>
        )}
        <div className="min-w-0">
          <h3 className="font-display text-[1.35rem] leading-tight font-semibold text-[var(--color-ink-900)]">
            {title}
          </h3>
          {subtitle && (
            <p className="mt-1 text-sm text-[var(--color-ink-500)] max-w-md">{subtitle}</p>
          )}
        </div>
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  );
}
