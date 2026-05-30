import { cn } from "@/lib/utils";

interface JsonViewerProps {
  value: unknown;
  className?: string;
  maxHeightClassName?: string;
}

// Lightweight pretty-print. Avoids pulling in a heavy syntax highlighter
// — the instantiate dialog needs a readable dump, not editor-grade syntax.
export function JsonViewer({
  value,
  className,
  maxHeightClassName = "max-h-96",
}: JsonViewerProps) {
  let text: string;
  try {
    text = typeof value === "string" ? value : JSON.stringify(value, null, 2);
  } catch {
    text = String(value);
  }

  return (
    <pre
      className={cn(
        "overflow-auto rounded-2xl border border-black/[0.06] bg-black/[0.03] p-4 font-mono text-[12px] leading-relaxed dark:border-white/10 dark:bg-white/[0.03]",
        maxHeightClassName,
        className
      )}
    >
      {text}
    </pre>
  );
}
