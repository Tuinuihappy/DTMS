// Centralized date/time formatting for DTMS frontend.
//
// Backend convention: all timestamps arrive as ISO-8601 UTC strings (with `Z`).
// Display convention: ISO-like format `YYYY-MM-DD HH:mm` in the user's browser
// timezone. Locale fixed to en-US for relative time.
//
// Manual construction (not Intl.DateTimeFormat) is intentional — it keeps the
// output deterministic, sortable as a string, and avoids locale variance.

export type DateInput = string | number | Date | null | undefined;

export const DATETIME_EMPTY = "—";

const p2 = (n: number) => String(n).padStart(2, "0");

function toDate(value: DateInput): Date | null {
  if (value == null || value === "") return null;
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** `2026-06-21` */
export function formatDate(value: DateInput): string {
  const d = toDate(value);
  if (!d) return DATETIME_EMPTY;
  return `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())}`;
}

/** `14:30` */
export function formatTime(value: DateInput): string {
  const d = toDate(value);
  if (!d) return DATETIME_EMPTY;
  return `${p2(d.getHours())}:${p2(d.getMinutes())}`;
}

/** `2026-06-21 14:30` — default display for tables, cards, drawers. */
export function formatDateTime(value: DateInput): string {
  const d = toDate(value);
  if (!d) return DATETIME_EMPTY;
  return `${formatDate(d)} ${formatTime(d)}`;
}

/** `2026-06-21 14:30:45` — for audit logs and tooltips where seconds matter. */
export function formatDateTimeSeconds(value: DateInput): string {
  const d = toDate(value);
  if (!d) return DATETIME_EMPTY;
  return `${formatDateTime(d)}:${p2(d.getSeconds())}`;
}

const SEC = 1000;
const MIN = 60_000;
const HR = 3_600_000;
const DAY = 86_400_000;
const WK = 7 * DAY;
const MO = 30 * DAY;
const YR = 365 * DAY;

const rtf = new Intl.RelativeTimeFormat("en-US", { numeric: "auto" });

/** `3 minutes ago` / `in 5 hours` — uses Intl.RelativeTimeFormat. */
export function formatRelative(value: DateInput): string {
  const d = toDate(value);
  if (!d) return DATETIME_EMPTY;
  const diff = d.getTime() - Date.now();
  const abs = Math.abs(diff);
  if (abs < MIN) return rtf.format(Math.round(diff / SEC), "second");
  if (abs < HR) return rtf.format(Math.round(diff / MIN), "minute");
  if (abs < DAY) return rtf.format(Math.round(diff / HR), "hour");
  if (abs < WK) return rtf.format(Math.round(diff / DAY), "day");
  if (abs < MO) return rtf.format(Math.round(diff / WK), "week");
  if (abs < YR) return rtf.format(Math.round(diff / MO), "month");
  return rtf.format(Math.round(diff / YR), "year");
}

/** ISO UTC → `YYYY-MM-DDTHH:mm` for `<input type="datetime-local">`. */
export function toDateTimeLocalInput(value: DateInput): string {
  const d = toDate(value);
  if (!d) return "";
  return `${formatDate(d)}T${formatTime(d)}`;
}

/** `YYYY-MM-DDTHH:mm` from `<input type="datetime-local">` → ISO UTC string. */
export function fromDateTimeLocalInput(local: string): string | null {
  if (!local) return null;
  const d = new Date(local);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Numeric timestamp for sorting; returns 0 for null/invalid. */
export function timestamp(value: DateInput): number {
  const d = toDate(value);
  return d ? d.getTime() : 0;
}

export const compareDatesAsc = (a: DateInput, b: DateInput) =>
  timestamp(a) - timestamp(b);

export const compareDatesDesc = (a: DateInput, b: DateInput) =>
  timestamp(b) - timestamp(a);
