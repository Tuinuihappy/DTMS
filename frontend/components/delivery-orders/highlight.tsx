"use client";

// Wraps the substrings of `text` that match `query` with a styled <mark>.
// Match is case-insensitive and substring-based — same semantics as the
// backend ILIKE filter, so the highlight always corresponds to why the
// row showed up.
//
// Empty/short queries (< 1 char after trim) render the text as-is. We
// escape the query before building the regex so user input like
// "WO-LEGACY (special.*)" can't blow up the matcher.
export function Highlight({
  text,
  query,
  className,
}: {
  text: string | null | undefined;
  query: string;
  className?: string;
}) {
  if (!text) return null;
  const q = query.trim();
  if (q.length === 0) return <span className={className}>{text}</span>;

  const escaped = q.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const parts = text.split(new RegExp(`(${escaped})`, "ig"));
  const needleLower = q.toLowerCase();

  return (
    <span className={className}>
      {parts.map((part, i) =>
        part.toLowerCase() === needleLower ? (
          <mark
            key={i}
            className="rounded-[3px] bg-[var(--color-amber-soft)] px-[2px] py-0 font-semibold text-[var(--color-amber)]"
          >
            {part}
          </mark>
        ) : (
          <span key={i}>{part}</span>
        ),
      )}
    </span>
  );
}
