// Build-time guard: every top-level app/ route group must be either listed
// in proxy.ts's matcher or declared public below.
//
// The matcher is an explicit allowlist (see the rationale in proxy.ts), so
// its one weakness is a new route group nobody remembers to add. This runs
// as `prebuild`, which means `npm run build` and the Docker image build both
// fail loudly instead of shipping an unguarded page.
//
// Zero dependencies — plain node:fs so it runs before install-time tooling.

import { readFileSync, readdirSync, existsSync, statSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const APP_DIR = join(root, "app");
const PROXY_FILE = join(root, "proxy.ts");

// Route groups that are intentionally reachable without a session.
// `/` (app/page.tsx) is the marketing landing and has no directory, so it
// never appears in the scan.
const PUBLIC_TOP_LEVEL = new Set(["login"]);

function matcherPrefixes() {
  const src = readFileSync(PROXY_FILE, "utf8");
  const block = src.match(/matcher:\s*\[([\s\S]*?)\]/);
  if (!block) {
    console.error("check-route-protection: no `matcher: [...]` found in proxy.ts");
    process.exit(1);
  }
  return new Set(
    [...block[1].matchAll(/["'`]\/([^/"'`]+)/g)].map((m) => m[1]),
  );
}

// A directory "is a route" if it (or anything under it) has a page file.
function hasPage(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isFile() && /^page\.(tsx|ts|jsx|js)$/.test(entry.name)) return true;
    if (entry.isDirectory() && hasPage(join(dir, entry.name))) return true;
  }
  return false;
}

if (!existsSync(APP_DIR) || !statSync(APP_DIR).isDirectory()) {
  console.error(`check-route-protection: ${APP_DIR} not found`);
  process.exit(1);
}

const prefixes = matcherPrefixes();
const unguarded = [];

for (const entry of readdirSync(APP_DIR, { withFileTypes: true })) {
  if (!entry.isDirectory()) continue;
  const name = entry.name;
  // `api` authenticates per-route and must return 401 rather than redirect.
  // Private folders (_x), route groups ((x)), slots (@x) and dynamic
  // segments ([x]) are not addressable top-level prefixes.
  if (name === "api" || /^[_(@[]/.test(name)) continue;
  if (!hasPage(join(APP_DIR, name))) continue;
  if (prefixes.has(name) || PUBLIC_TOP_LEVEL.has(name)) continue;
  unguarded.push(name);
}

if (unguarded.length > 0) {
  console.error(
    "\ncheck-route-protection: these app/ route groups are neither in the\n" +
      "proxy.ts matcher nor declared public:\n" +
      unguarded.map((n) => `  - /${n}`).join("\n") +
      "\n\nAdd \"/<name>/:path*\" to config.matcher in proxy.ts, or add the name\n" +
      "to PUBLIC_TOP_LEVEL in scripts/check-route-protection.mjs if the route\n" +
      "is meant to be reachable without signing in.\n",
  );
  process.exit(1);
}

console.log(
  `check-route-protection: ok (${prefixes.size} guarded prefixes, ${PUBLIC_TOP_LEVEL.size} public)`,
);
