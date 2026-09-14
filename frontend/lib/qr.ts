/**
 * QR generation for carrier labels.
 *
 * The payload is the bare `CarrierCode` — not a URL, not a GUID. A URL would
 * bake this deployment's hostname into every sticker in the warehouse, and a
 * GUID cannot be typed by hand when a label gets scraped off a shelf. The code
 * is already immutable and unique forever, which is exactly what a printed
 * label needs.
 *
 * `qrcode` is imported dynamically so its ~50KB never lands in the main bundle:
 * only the labels page and anything else that actually draws a code pays for it.
 */

/** Error correction level Q — recovers 25% damage. Shelf labels get wiped,
 *  scraped and knocked; M would be lighter but this is the cheap half of
 *  making a sticker survive a warehouse. */
const ERROR_CORRECTION = "Q" as const;

/** 4 modules, the spec minimum. Cutting the quiet zone flush to the edge is the
 *  single most common reason a printed QR refuses to scan. */
const QUIET_ZONE_MODULES = 4;

export async function carrierQrSvg(carrierCode: string): Promise<string> {
  const { toString } = await import("qrcode");
  return toString(carrierCode, {
    type: "svg",
    errorCorrectionLevel: ERROR_CORRECTION,
    margin: QUIET_ZONE_MODULES,
  });
}

// ── Downloadable label ─────────────────────────────────────────────────────
// Laid out in SVG units; the file is vector, so these set proportions, not a
// print size. Proportions follow the card on the labels page.

const LABEL_WIDTH = 400;
const LABEL_SIDE_PADDING = 24;
const CODE_FONT_SIZE = 36;
const NAME_FONT_SIZE = 26;

// A downloaded file is opened outside the app, where the page's web fonts do
// not exist. Each stack starts with the font the page uses and falls back to
// ones every Windows machine has. The name stack includes Thai-capable faces
// (Leelawadee UI, Tahoma) because display names are often typed in Thai — a
// fallback without Thai glyphs would print boxes.
const CODE_FONT_STACK =
  "'Google Sans Code', ui-monospace, 'Cascadia Mono', Consolas, 'Courier New', monospace";
const NAME_FONT_STACK =
  "'Google Sans', 'Leelawadee UI', 'Segoe UI', Tahoma, Arial, sans-serif";

/**
 * The QR plus its code and name, as one self-contained SVG — the same card the
 * labels page shows, so what gets downloaded is what was on screen.
 *
 * The code is never truncated: it is the fallback a person types when the QR
 * is scraped off, so a code too long for the width is set smaller instead.
 * The display name is decoration and is cut short with an ellipsis.
 */
export function carrierLabelSvg(
  qrSvg: string,
  carrierCode: string,
  displayName?: string | null,
): string {
  const body = qrSvg.trim().replace(/^<\?xml[^>]*\?>\s*/, "");
  if (!body.startsWith("<svg")) {
    // Nesting depends on the library emitting a bare <svg> root. Fail loudly
    // rather than hand someone a label with the QR stretched over its text.
    throw new Error("Unexpected QR output; cannot build the label.");
  }

  const size = LABEL_WIDTH;
  const usable = size - LABEL_SIDE_PADDING * 2;
  const center = size / 2;

  // The QR already carries its quiet zone, so the code sits just below it.
  const codeBaseline = size + 40;
  const name = displayName?.trim() ? ellipsize(displayName.trim(), 24) : null;
  const nameBaseline = codeBaseline + 38;
  const height = (name ? nameBaseline : codeBaseline) + 28;

  // Monospace glyphs run 0.55-0.6em wide across the fallback stack; 0.62 leaves
  // room for the widest. Shrinking the font rather than using SVG textLength:
  // textLength is ignored by several viewers a label file gets opened in, and
  // there the code simply runs off both edges.
  const codeFontSize = Math.min(
    CODE_FONT_SIZE,
    Math.floor(usable / (carrierCode.length * 0.62)),
  );

  return [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${height}" viewBox="0 0 ${size} ${height}">`,
    `<rect width="${size}" height="${height}" fill="#ffffff"/>`,
    body.replace(/^<svg\b/, `<svg x="0" y="0" width="${size}" height="${size}"`),
    `<text x="${center}" y="${codeBaseline}" text-anchor="middle" font-family="${CODE_FONT_STACK}" font-size="${codeFontSize}" font-weight="700" fill="#000000">${escapeXml(carrierCode)}</text>`,
    name
      ? `<text x="${center}" y="${nameBaseline}" text-anchor="middle" font-family="${NAME_FONT_STACK}" font-size="${NAME_FONT_SIZE}" fill="#525252">${escapeXml(name)}</text>`
      : "",
    `</svg>`,
  ].join("");
}

function ellipsize(text: string, max: number): string {
  const chars = Array.from(text);
  return chars.length > max ? `${chars.slice(0, max - 1).join("")}…` : text;
}

/** Display names are free text — a name containing & or < would otherwise
 *  produce a file no viewer will open. */
function escapeXml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

/**
 * Hands the browser one SVG file. Mirrors the CSV export idiom in
 * `orders-experience.tsx` — the only download pattern this project has.
 */
export function downloadSvg(fileName: string, svg: string): void {
  const url = URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
