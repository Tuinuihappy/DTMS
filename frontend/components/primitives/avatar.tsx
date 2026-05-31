import { cn, initials } from "@/lib/utils";

type Props = {
  name: string;
  hue?: number;
  size?: "xs" | "sm" | "md" | "lg";
  ring?: boolean;
  className?: string;
};

const sizeMap = {
  xs: "h-6 w-6 text-[10px]",
  sm: "h-8 w-8 text-xs",
  md: "h-10 w-10 text-sm",
  lg: "h-12 w-12 text-base",
} as const;

export function Avatar({ name, hue = 220, size = "md", ring = false, className }: Props) {
  const bg = `linear-gradient(135deg, hsl(${hue} 70% 75%) 0%, hsl(${(hue + 40) % 360} 80% 60%) 100%)`;
  return (
    <span
      className={cn(
        "relative inline-flex items-center justify-center rounded-full font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.4),0_2px_6px_rgba(15,23,42,0.18)] select-none",
        sizeMap[size],
        ring && "ring-2 ring-white",
        className,
      )}
      style={{ background: bg }}
      aria-label={name}
      title={name}
    >
      {initials(name)}
    </span>
  );
}

export function AvatarStack({
  people,
  size = "sm",
  max = 4,
}: {
  people: { name: string; avatarHue?: number; hue?: number }[];
  size?: "xs" | "sm" | "md";
  max?: number;
}) {
  const visible = people.slice(0, max);
  const remaining = Math.max(0, people.length - max);
  const overlap = size === "xs" ? "-ml-1.5" : size === "sm" ? "-ml-2" : "-ml-2.5";
  return (
    <div className="flex items-center">
      {visible.map((p, i) => (
        <Avatar
          key={i}
          name={p.name}
          hue={p.avatarHue ?? p.hue ?? 220}
          size={size}
          ring
          className={i === 0 ? "" : overlap}
        />
      ))}
      {remaining > 0 && (
        <span
          className={cn(
            "inline-flex items-center justify-center rounded-full bg-[var(--color-ink-100)] text-[var(--color-ink-600)] font-semibold ring-2 ring-white",
            sizeMap[size === "xs" ? "xs" : size === "sm" ? "sm" : "md"],
            overlap,
          )}
        >
          +{remaining}
        </span>
      )}
    </div>
  );
}
