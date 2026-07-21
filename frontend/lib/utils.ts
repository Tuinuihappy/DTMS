import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatNumber(n: number, opts?: Intl.NumberFormatOptions) {
  return new Intl.NumberFormat("en-US", opts).format(n);
}

// Search-text normalizer for picker comboboxes: lowercase + strip
// separators (underscore, hyphen, whitespace) so "STF02", "stf 02" and
// "STF-02" all match a stored code like "STF_02". Callers must treat an
// empty normalized query as "no filter" (station codes can be
// separator-only after stripping).
export function normalizeSearchText(s: string) {
  return s.toLowerCase().replace(/[_\-\s]/g, "");
}

export function initials(name: string) {
  return name
    .split(" ")
    .map((p) => p[0])
    .filter(Boolean)
    .slice(0, 2)
    .join("")
    .toUpperCase();
}
