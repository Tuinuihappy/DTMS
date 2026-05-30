import { cn } from "@/lib/utils";
import type { OrderStatus } from "@/types/delivery-order";

// Single source of truth for the colour each lifecycle status reads as.
// All values use Tailwind colour classes with a translucent bg so they
// land on the liquid-glass surface without clashing.
const STATUS_STYLES: Record<OrderStatus, string> = {
  Draft: "bg-zinc-500/15 text-zinc-700 dark:bg-zinc-400/15 dark:text-zinc-300",
  Submitted: "bg-blue-500/15 text-blue-700 dark:bg-blue-400/15 dark:text-blue-300",
  Validated: "bg-cyan-500/15 text-cyan-700 dark:bg-cyan-400/15 dark:text-cyan-300",
  Confirmed:
    "bg-emerald-500/15 text-emerald-700 dark:bg-emerald-400/15 dark:text-emerald-300",
  Planning:
    "bg-violet-500/15 text-violet-700 dark:bg-violet-400/15 dark:text-violet-300",
  Planned:
    "bg-indigo-500/15 text-indigo-700 dark:bg-indigo-400/15 dark:text-indigo-300",
  Dispatched: "bg-sky-500/15 text-sky-700 dark:bg-sky-400/15 dark:text-sky-300",
  InProgress:
    "bg-orange-500/15 text-orange-700 dark:bg-orange-400/15 dark:text-orange-300",
  Completed:
    "bg-emerald-600/20 text-emerald-800 dark:bg-emerald-500/20 dark:text-emerald-300",
  Held: "bg-amber-500/15 text-amber-700 dark:bg-amber-400/15 dark:text-amber-300",
  Rejected: "bg-red-500/15 text-red-700 dark:bg-red-400/15 dark:text-red-300",
  Cancelled: "bg-zinc-600/15 text-zinc-600 dark:bg-zinc-500/15 dark:text-zinc-400",
  Amended:
    "bg-fuchsia-500/15 text-fuchsia-700 dark:bg-fuchsia-400/15 dark:text-fuchsia-300",
  Failed: "bg-rose-500/15 text-rose-700 dark:bg-rose-400/15 dark:text-rose-300",
};

interface StatusBadgeProps {
  status: OrderStatus;
  className?: string;
}

export function StatusBadge({ status, className }: StatusBadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide",
        STATUS_STYLES[status],
        className
      )}
    >
      {status}
    </span>
  );
}
