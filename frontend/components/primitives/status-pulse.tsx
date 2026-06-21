import { cn } from "@/lib/utils";

type Props = {
  tone?: "success" | "amber" | "coral" | "brand" | "live";
  size?: "sm" | "md";
  className?: string;
};

const toneClass: Record<NonNullable<Props["tone"]>, string> = {
  success: "text-[var(--color-success)] bg-[var(--color-success)]",
  amber: "text-[var(--color-amber)] bg-[var(--color-amber)]",
  coral: "text-[var(--color-coral)] bg-[var(--color-coral)]",
  brand: "text-[var(--color-brand-500)] bg-[var(--color-brand-500)]",
  live: "text-[var(--color-live)] bg-[var(--color-live)]",
};

export function StatusPulse({ tone = "success", size = "sm", className }: Props) {
  const dim = size === "sm" ? "h-2 w-2" : "h-2.5 w-2.5";
  return (
    <span
      className={cn(
        "relative inline-flex rounded-full animate-pulse-ring",
        toneClass[tone],
        dim,
        className,
      )}
      aria-hidden
    />
  );
}
