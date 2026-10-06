// Build-time guard: overlays go through the layer stack (lib/overlay).
//
// Flags, in components/:
//   - a keydown listener added by hand that handles Escape — overlays use
//     ModalLayer / useDialog, lists and menus use useDismissableLayer, so
//     the stack can send Esc to the topmost layer only;
//   - hand-written aria-modal — ModalLayer sets it. (A plain role="dialog"
//     without aria-modal is fine: that's a non-modal popover, e.g. the map
//     instructions panel, which uses useDismissableLayer.)
//
// No eslint in this repo, so without this a new overlay would quietly go
// back to a window listener and one Esc would close every layer again.
//
//   node scripts/check-overlay-keys.mjs           fail (exit 1) on findings
//   node scripts/check-overlay-keys.mjs --report  list findings, exit 0
//
// Zero dependencies — plain node:fs, like check-route-protection.mjs.

import { readFileSync, readdirSync } from "node:fs";
import { join, dirname, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const SCAN = join(root, "components");

// Files allowed to do this themselves: the frame that owns the attributes.
const ALLOW = new Set(["components/primitives/modal-layer.tsx"]);

function* walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(p);
    else if (/\.(tsx|ts)$/.test(entry.name)) yield p;
  }
}

const findings = [];
for (const file of walk(SCAN)) {
  const rel = relative(root, file).split(sep).join("/");
  if (ALLOW.has(rel)) continue;
  const lines = readFileSync(file, "utf8").split(/\r?\n/);
  lines.forEach((line, i) => {
    if (/addEventListener\(\s*["']keydown["']/.test(line)) {
      // Look at the handler around it for an Escape check.
      const around = lines.slice(Math.max(0, i - 12), i + 1).join("\n");
      if (/["']Escape["']/.test(around)) {
        findings.push(`${rel}:${i + 1}  hand-rolled Escape keydown listener — use ModalLayer / useDialog / useDismissableLayer`);
      }
    }
    if (/aria-modal/.test(line)) {
      findings.push(`${rel}:${i + 1}  hand-written aria-modal — use ModalLayer (it sets it)`);
    }
  });
}

const report = process.argv.includes("--report");
if (findings.length === 0) {
  console.log("check-overlay-keys: ok");
  process.exit(0);
}
console[report ? "log" : "error"](
  `check-overlay-keys: ${findings.length} finding(s)\n` + findings.map((f) => "  " + f).join("\n"),
);
process.exit(report ? 0 : 1);
