---
description: Review frontend code and UI/UX — correctness, Next 16/React 19 patterns, DTMS house rules, accessibility, performance, and design consistency. Read-only; reports findings ranked by severity.
argument-hint: <path, route, component, or "diff" (default — uncommitted + last commit under frontend/)>
---

# Frontend review — $ARGUMENTS

You are reviewing, not fixing. The deliverable is a findings report in chat. Do not edit files unless the user replies asking for fixes.

A review is good only if every finding is **real, located, and actionable**: a developer can open `file:line`, see the problem, and know the fix without re-investigating. Ten verified findings beat forty plausible ones.

---

## Hard rules

1. **Read before you judge.** Every finding cites `frontend/path/file.tsx:123` from a file you actually opened this session. No finding from a filename or a grep hit alone.
2. **Verify, then report.** For each candidate finding, re-read the surrounding code and ask: is this already handled elsewhere (a parent, a primitive, a hook, the API proxy)? If you can't rule that out, mark it `PLAUSIBLE`, not `CONFIRMED`.
3. **Read-only.** Read, Grep, Glob, read-only Bash, and `Explore` subagents. You may run `npm run typecheck` in `frontend/`. Do not run `npm run lint` (no eslint is installed — the script is dead), do not run `next build`, do not start the dev server unless the user asked for a visual check.
4. **Respect decisions already made.** The DTMS rules below are deliberate. Flag code that breaks them; never recommend undoing them.
5. **No taste-only nits.** A design finding must name the concrete cost (unreadable in dark mode, inconsistent with N other screens, fails contrast, mis-tap on mobile). "Could look more modern" is not a finding.

---

## Stage 0 — Scope

Resolve `$ARGUMENTS`:

- empty or `diff` → `git diff HEAD -- frontend/` plus `git show --stat HEAD -- frontend/`; review changed files, reading enough of their callers/callees to judge them.
- a path or component → that file/dir plus its direct imports from `components/primitives`, `lib/hooks`, `lib/api`.
- a route (e.g. `/dispatch`, `/m/trips`) → `app/<route>/**` and the `components/<feature>/` it renders.

State the scope in one line before reviewing. If it exceeds ~25 files, split it by feature folder across `general-purpose` subagents (not `Explore` — it locates code, it doesn't review it). Give each subagent this file's checklists, tell it to stay read-only and return findings in the Stage 4 format, then verify each finding yourself before merging.

---

## Stage 1 — Code review checklist

### Correctness & React 19
- Hook dependency arrays — **check by hand**; there is no exhaustive-deps lint. Stale closures in callbacks, effects, intervals, SignalR handlers.
- Effects that should be derived state or event handlers; missing cleanup (timers, listeners, hub subscriptions, AbortController).
- Race conditions: out-of-order fetch responses, setState after unmount, double-submit on dialogs (button not disabled while pending).
- Keys: index-as-key on reorderable/filterable lists.
- Error/empty/loading states: every async view handles all three; errors surface the server's message from `lib/api/errors.ts`, not a generic toast.

### Next 16 / server–client boundary
- `"use client"` only where needed; no server-only module (`lib/auth/server-session.ts`) imported into client code.
- Data fetching goes through `lib/api/*` and the `app/api` proxy — no direct calls to the backend origin from the browser.
- Hydration mismatches: `Date.now()`, locale formatting, `window` reads during render. Dates go through `lib/datetime.ts` / `primitives/date-time.tsx`.

### DTMS house rules (break = finding)
- **Overlays**: every modal/drawer backdrop uses `components/primitives/overlay-backdrop.tsx`. A hand-rolled backdrop inside `AnimatePresence` strands an invisible click-blocking div — HIGH severity.
- **Scanning**: barcode/QR input uses `lib/hooks/use-wedge-scanner.ts` inside a modal; scans never auto-commit (user confirms). No camera/zxing/BarcodeDetector.
- **Route protection**: new pages must be in the `proxy.ts` prefix allowlist (`scripts/check-route-protection.mjs` enforces at prebuild). `proxy.ts` must never match `/api` — `public/sw.js` drains its offline queue on a 200.
- **Session**: cookie access only through `lib/auth/session.ts` helpers (the session cookie is chunked `.0/.1/…`); never read/write `dtms_session` directly.
- **Permissions**: UI gating via `lib/auth/permissions.ts`; hiding a button is not authorization — confirm the API route also checks.
- **Realtime**: hub subscriptions via `lib/hooks/use-hub-subscription.ts`; polling via `use-poll-schedule` / `use-projection-poll`. No ad-hoc `setInterval` fetch loops.
- **Order identity**: `OrderRef` is the external reference shown to users; don't invent or display duplicate ID fields.
- **Motion**: `motion` v12 — no regression to framer-motion imports; animate transform/opacity, not layout properties, on lists.

### Security
- `dangerouslySetInnerHTML`, unescaped user/vendor strings, `href` from data without scheme check, tokens or PII in `console.*`, secrets in `NEXT_PUBLIC_*`.

### Performance
- Heavy components (recharts, lightbox, QR) not lazy-loaded; large lists without virtualization or pagination; re-renders caused by inline object/array props into memoized children; images without size hints (`lib/image-compress.ts` / `image-preload.ts` exist — use them).

---

## Stage 2 — UI/UX review checklist

### Design system consistency
- Colors from the tokens in `app/globals.css` (`--color-ink-*`, `brand-*`, `surface*`, `amber/coral/success/info/live`) — raw hex or arbitrary Tailwind colors are a finding.
- Fonts: `font-sans` / `font-display` / `font-mono` (Google Sans / Google Sans Code). IDs, codes, and numbers in mono with tabular figures.
- Reuse existing primitives before custom markup: `data-table/*` (incl. `table-empty-state`, `table-skeleton`, `mobile-card-row`), `glass-card`, `section-label`, `status-pulse`, `id-cell`, the comboboxes. A one-off reimplementation is a finding.
- Status colors mean the same thing on every screen (amber = attention, coral = failure, success = done, live = realtime).

### Dark mode
- Every surface readable under `.dark`. `globals.css` notes that `bg-white/X`-style utilities need explicit `dark:` variants — check for missing ones.

### Accessibility
- Interactive elements are `<button>`/`<a>`, not clickable `<div>`; icon-only buttons have `aria-label`.
- Dialogs: focus moves in on open, returns on close, Esc closes, focus trapped.
- Contrast ≥ 4.5:1 for body text (watch `ink-300/400` on `surface-soft`), visible focus ring, form fields with labels and error text linked via `aria-describedby`.
- Thai text: no fixed heights that clip ascenders/descenders (สระบน/วรรณยุกต์); no `truncate` on content operators must read in full without a tooltip.

### Operator PWA (`app/m/**`, `components/operator-pwa/**`)
- Touch targets ≥ 44px, primary action reachable one-handed, works at 360px width, no hover-only affordances.
- Offline/slow network: pending state visible, retry obvious, no silent loss of a submitted action.

### UX flow
- Destructive actions (cancel order, cancel trip, resend) confirm and say what will happen.
- Feedback within 100ms for every click; long operations show progress; success is confirmed, not assumed.
- Tables: sortable where users compare, filters persist across navigation where expected, empty state explains what to do next.

---

## Stage 3 — Verify

1. Run `npm run typecheck` in `frontend/`; report any errors in scope as findings (quote the compiler line).
2. Re-check each candidate against Hard rule 2. Drop what you can't defend.
3. If a finding depends on runtime behaviour you couldn't observe, say so in its text.

---

## Stage 4 — Report

Write the report in **Thai**; keep code identifiers, file paths, and API names in English.

```
## สรุป
<scope 1 บรรทัด> · <จำนวน findings ตาม severity> · typecheck: ผ่าน/ไม่ผ่าน

## Findings
### [HIGH|MEDIUM|LOW] <หัวข้อสั้น> — CONFIRMED|PLAUSIBLE
- ที่: frontend/path/file.tsx:123
- ปัญหา: <เกิดอะไร ในสถานการณ์ไหน>
- ผลกระทบ: <ผู้ใช้/ระบบเจออะไร>
- แก้: <วิธีแก้ที่เจาะจง อ้าง primitive/hook ที่มีอยู่ถ้ามี>

## สิ่งที่ทำได้ดี
<2–3 ข้อ สั้นๆ — เฉพาะที่ควรใช้เป็นแบบอย่าง>

## ไม่ได้ตรวจ
<สิ่งที่อยู่นอก scope หรือต้องดูตอนรันจริง>
```

Severity:
- **HIGH** — bug users will hit, data loss, security issue, blocked interaction, broken house rule with known failure mode.
- **MEDIUM** — wrong under specific conditions, a11y barrier, inconsistency visible across screens, noticeable perf cost.
- **LOW** — maintainability, minor inconsistency, polish.

Order findings HIGH → LOW. End by asking whether to fix any of them — do not start fixing.
