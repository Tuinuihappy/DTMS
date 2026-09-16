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

// ── PNG label ──────────────────────────────────────────────────────────────
// For sticker printer software that cannot open SVG. The same card as
// carrierLabelSvg, scaled 3×: 1200px wide prints sharp up to about 10 cm at
// 300 DPI, and a printer dialog can scale it down from there.

const PNG_WIDTH = 1200;
const PNG_SCALE = PNG_WIDTH / LABEL_WIDTH;
// The code may shrink to fit, but not past where a person can still read it.
const PNG_MIN_CODE_FONT = 40;

/**
 * The label as a PNG, drawn directly onto a canvas rather than by rasterising
 * the SVG. Two reasons:
 *
 * - The QR is laid out in whole pixels per module. Scaling a vector QR to an
 *   arbitrary width leaves anti-aliased grey edges between modules, and a
 *   scanner has to guess where a grey seam belongs.
 * - Text uses the page's own loaded fonts. An SVG drawn through an image
 *   cannot reach web fonts, so the code would come out in whatever the
 *   fallback happens to be.
 */
export async function carrierLabelPng(
  carrierCode: string,
  displayName?: string | null,
): Promise<Blob> {
  const { create } = await import("qrcode");
  const { modules } = create(carrierCode, { errorCorrectionLevel: ERROR_CORRECTION });

  const cells = modules.size + QUIET_ZONE_MODULES * 2;
  const modulePx = Math.floor(PNG_WIDTH / cells);
  const qrPx = modulePx * cells;
  // Whatever width whole modules cannot fill becomes extra white either side.
  const qrLeft = Math.floor((PNG_WIDTH - qrPx) / 2);

  const usable = PNG_WIDTH - LABEL_SIDE_PADDING * PNG_SCALE * 2;
  const codeBaseline = qrPx + 40 * PNG_SCALE;
  const name = displayName?.trim() || null;
  const nameBaseline = codeBaseline + 38 * PNG_SCALE;
  const height = (name ? nameBaseline : codeBaseline) + 28 * PNG_SCALE;

  const canvas = document.createElement("canvas");
  canvas.width = PNG_WIDTH;
  canvas.height = height;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("This browser cannot draw the label.");

  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, PNG_WIDTH, height);

  ctx.fillStyle = "#000000";
  for (let row = 0; row < modules.size; row++) {
    for (let col = 0; col < modules.size; col++) {
      if (modules.get(row, col)) {
        ctx.fillRect(
          qrLeft + (col + QUIET_ZONE_MODULES) * modulePx,
          (row + QUIET_ZONE_MODULES) * modulePx,
          modulePx,
          modulePx,
        );
      }
    }
  }

  const codeFamily = withPageFont("--font-mono", CODE_FONT_STACK);
  const nameFamily = withPageFont("--font-sans", NAME_FONT_STACK);
  const codeFont = (size: number) => `700 ${size}px ${codeFamily}`;
  const nameFont = `400 ${NAME_FONT_SIZE * PNG_SCALE}px ${nameFamily}`;

  // A canvas draws with whatever is loaded at that instant. Ask for exactly
  // these glyphs first, or the first label after a page load comes out in the
  // fallback font.
  await Promise.all([
    document.fonts.load(codeFont(CODE_FONT_SIZE * PNG_SCALE), carrierCode),
    name ? document.fonts.load(nameFont, name) : Promise.resolve(),
  ]).catch(() => {
    // Drawn with the fallback instead — still a usable label.
  });

  ctx.textAlign = "center";
  ctx.textBaseline = "alphabetic";

  // Never truncated, as in the SVG: shrink until it fits. Measured rather than
  // estimated, since the canvas knows the real glyph widths.
  let codeSize = CODE_FONT_SIZE * PNG_SCALE;
  ctx.font = codeFont(codeSize);
  while (codeSize > PNG_MIN_CODE_FONT && ctx.measureText(carrierCode).width > usable) {
    codeSize -= 2;
    ctx.font = codeFont(codeSize);
  }
  ctx.fillText(carrierCode, PNG_WIDTH / 2, codeBaseline);

  if (name) {
    ctx.font = nameFont;
    ctx.fillStyle = "#525252";
    ctx.fillText(fitWithEllipsis(ctx, name, usable), PNG_WIDTH / 2, nameBaseline);
  }

  return new Promise((resolve, reject) =>
    canvas.toBlob(
      (blob) => (blob ? resolve(blob) : reject(new Error("Could not encode the PNG."))),
      "image/png",
    ),
  );
}

/** The page's font first, then the stack that also works outside the app. */
function withPageFont(variable: string, fallback: string): string {
  const page = getComputedStyle(document.body).getPropertyValue(variable).trim();
  return page ? `${page}, ${fallback}` : fallback;
}

/** Cuts by measured width rather than a character count: Thai marks take no
 *  width of their own, so a count would cut far too early or too late. */
function fitWithEllipsis(ctx: CanvasRenderingContext2D, text: string, width: number): string {
  if (ctx.measureText(text).width <= width) return text;
  const chars = Array.from(text);
  while (chars.length > 1 && ctx.measureText(`${chars.join("")}…`).width > width) chars.pop();
  return `${chars.join("")}…`;
}

/**
 * Hands the browser one file. Mirrors the CSV export idiom in
 * `orders-experience.tsx` — the only download pattern this project has.
 */
export function downloadBlob(fileName: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

export function downloadSvg(fileName: string, svg: string): void {
  downloadBlob(fileName, new Blob([svg], { type: "image/svg+xml" }));
}
