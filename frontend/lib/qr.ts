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
