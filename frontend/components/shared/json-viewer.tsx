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
        "overflow-auto rounded-md border bg-muted/30 p-3 font-mono text-xs leading-5",
        maxHeightClassName,
        className
      )}
    >
      {text}
    </pre>
  );
}
