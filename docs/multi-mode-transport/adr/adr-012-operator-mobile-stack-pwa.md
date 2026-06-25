# ADR-012: Operator Mobile Stack — PWA (Progressive Web App)

- **Status**: Accepted
- **Date**: 2026-06-25
- **Deciders**: Solo dev (DTMS)
- **Supersedes**: Partially supersedes [ADR-011 Frontend Architecture](adr-011-frontend-architecture.md) (mobile portion)
- **Related**: [ADR-013 Push Notification](adr-013-push-notification-web-push.md), [ADR-014 Mobile API Authentication](adr-014-mobile-api-authentication-external-auth.md), [ADR-015 POD Upload Presigned](adr-015-pod-upload-presigned-urls.md), [Phase 4: Transport.Manual](../phases/phase-4-transport-manual.md)

## Context

Phase 4 (Manual transport mode) introduces operator mobile app. Operators (warehouse staff) need to:

- รับ job notification ที่ assigned ให้
- Acknowledge / Reject งาน
- Capture POD (photo + signature) ที่ pickup + drop
- Scan barcode / QR สำหรับ items
- GPS verify geofence
- Operate offline-capable (warehouse WiFi often spotty)

**Context shift since 2026-06-22 ADRs:**
- DTMS เป็น **solo-dev pre-launch** product (per memory `project_solo_developer.md`)
- Time-to-validate Manual mode workflow > time-to-production-polish
- Existing Next.js dispatcher console = 80% of UI components reusable
- External Auth API exists (ADR-014) → no DTMS internal auth needed

Original [ADR-011](adr-011-frontend-architecture.md) (2026-06-22) implicitly assumed native mobile (RN/Flutter/Native) without explicitly choosing — focused on dispatcher console structure. ADR-005 + ADR-007 assumed native stack downstream.

## Decision

ใช้ **Progressive Web App (PWA)** for operator app — extend existing Next.js codebase with mobile-optimized routes + Service Worker.

### Stack
- **Framework**: Next.js 16 (existing dispatcher console framework)
- **Mobile-optimized routes**: `/m/*` route group (operator-facing pages)
- **Service Worker**: workbox-based, handles push + offline caching
- **State**: existing React Query + Zustand patterns
- **Camera**: `navigator.mediaDevices.getUserMedia` + `<input type="file" capture>`
- **Signature**: `signature_pad` library on HTML5 canvas
- **Barcode**: ZXing-js (PWA fallback); enterprise Zebra hardware uses keyboard wedge (input fields)
- **GPS**: `navigator.geolocation`
- **Offline queue**: IndexedDB + Background Sync API
- **Auth**: External Auth JWT in IndexedDB (encrypted via Web Crypto)

### What gets shared with dispatcher console
- API client (`lib/api/*`) — 100% shared
- TypeScript types from backend — 100% shared
- Design tokens / Tailwind config — 100% shared
- Auth flow — shared (calls External Auth)
- Utility hooks — 80%+ shared
- UI primitives (Combobox, DateTime, Button) — 90% shared

### What's mobile-specific
- Touch-optimized layouts (larger tap targets, single-column flow)
- Service Worker for push + offline
- Camera/signature/GPS components
- Operator-specific routes + flows

## Reasoning — Why PWA over Native (RN/Flutter/Native)

### Solo dev + pre-launch context
| Mobile stack | MVP time | Codebase share | App Store wait |
|---|---|---|---|
| **PWA** ⭐ | **3-4 weeks** | **80% with Next.js** | **None** |
| React Native | 2-3 months | 30-50% | 1-3 days per release |
| Flutter | 3-4 months | 0% (Dart) | 1-3 days |
| Native (Swift+Kotlin) | 4-6 months | 0% | 1-3 days × 2 platforms |

### Capability check — does PWA do what we need?
| Capability | PWA support |
|---|---|
| Push notifications | ✅ Web Push API (iOS 16.4+, Android, Edge, Firefox) |
| Camera + photo | ✅ getUserMedia / capture attribute |
| Signature canvas | ✅ HTML5 canvas + signature_pad |
| GPS / geofence | ✅ navigator.geolocation |
| Barcode scan | 🟡 ZXing-js (~200ms) — slower than native but workable. Zebra hardware = keyboard wedge (instant) |
| Offline operation | ✅ Service Worker + IndexedDB + Background Sync |
| Secure token storage | ✅ Web Crypto + IndexedDB |
| Background processing | 🟡 Limited vs native — Background Sync covers most cases |
| Biometric auth | ✅ WebAuthn API (production-ready 2026) |

### Migration path (future-proof)
- Year 1 (now): PWA MVP → validate Manual workflow with real operators
- Year 2 (if validated): migrate to React Native — reuse 60-70% of React components (UI primitives, hooks, API client)
- Year 3+ (if scale > 500 ops): integrate native scanner SDKs (Zebra DataWedge, Honeywell SDK)

### What we lose vs native
- ~10% slower barcode scan (ZXing vs native ML Kit)
- No App Store / Play Store discoverability (operators install via URL + "Add to Home Screen")
- Less polished animations (no native widget set)
- iOS push only on 16.4+ (Sep 2023 release — 95%+ adoption by 2026)

### What we gain
- 1 codebase, 1 deploy pipeline, 1 dev's mental model
- Instant updates (operators get latest version on next refresh — no app store review)
- 80% code reuse from existing Next.js work
- No Mac required for development
- Lower TCO over 2 years

## Implementation Sketch

### Routes
```
frontend/
├── app/
│   ├── (console)/                    # existing dispatcher routes
│   │   └── ...
│   └── m/                            # NEW — operator PWA route group
│       ├── layout.tsx                # mobile-optimized shell
│       ├── login/page.tsx
│       ├── trips/
│       │   ├── page.tsx              # assigned trips list
│       │   └── [id]/
│       │       ├── page.tsx          # trip detail
│       │       ├── pickup/page.tsx   # POD capture flow
│       │       └── drop/page.tsx
│       ├── shift/page.tsx
│       └── settings/page.tsx
├── components/
│   ├── operator/                     # NEW mobile-specific components
│   │   ├── camera-capture.tsx
│   │   ├── signature-pad.tsx
│   │   ├── barcode-scanner.tsx
│   │   ├── geofence-status.tsx
│   │   └── offline-banner.tsx
│   └── ... (existing shared)
└── public/
    ├── manifest.json                 # PWA manifest
    └── sw.js                         # service worker (workbox build output)
```

### Service Worker scope
- Cache: API routes (stale-while-revalidate), static assets (cache-first)
- Push: receive notifications, show via `self.registration.showNotification()`
- Background Sync: retry failed POD uploads when network returns
- Update: prompt operator to refresh when new version available

### PWA installation flow
1. Operator visits `https://dtms.example.com/m/login` (URL or QR shared by dispatcher)
2. Browser detects manifest → prompts "Install" or "Add to Home Screen"
3. Operator approves → app icon on home screen, launches standalone (no browser chrome)
4. Login → External Auth API → JWT stored in IndexedDB → home screen

### Push subscription registration
1. After login → request `Notification.permission`
2. If granted → register Service Worker push subscription with VAPID public key
3. POST subscription to `/api/operator/devices/register-push`
4. DTMS stores subscription in `transport_manual.OperatorPushSubscriptions`

## Consequences

### Positive
- ✅ 3-4 week MVP (vs 2-3 months React Native)
- ✅ Single codebase, single deploy, single dev mental model
- ✅ Instant updates — no App Store review
- ✅ Code reuse: 80% of UI from dispatcher
- ✅ Lower 2-year TCO

### Negative
- ❌ Slower barcode scan than native (mitigation: Zebra keyboard wedge)
- ❌ No App Store presence (mitigation: dispatcher shares URL/QR for install)
- ❌ Less polished animations
- ❌ Some advanced native features (NFC, AR) not available

### Neutral
- 🟡 PWA on iOS = relies on Safari Web Push (Apple may restrict in future — but trend 2024-2026 = expanding support)
- 🟡 Operators need iOS 16.4+ / Android Chrome (modern device requirement)

## Migration Path (if PWA limits hit)

When ROI validates Manual mode AND we hit a PWA wall:
1. Phase A — Extract operator routes into separate Next.js app (already organized in `/m/*` route group)
2. Phase B — Replace Next.js routing with React Native Navigation, port React Query + Zustand verbatim
3. Phase C — Replace `getUserMedia` with `react-native-vision-camera`, `navigator.geolocation` with `@react-native-community/geolocation`
4. Phase D — Replace Web Push subscription with FCM/APNs (per future ADR superseding ADR-013)

Estimated migration time: 6-8 weeks (vs 2-3 months from scratch — savings = 60-70% reuse)

## Why this supersedes (parts of) ADR-011

ADR-011 (2026-06-22) focused on dispatcher console structure + per-mode component organization. It implicitly assumed mobile = native (referencing "operator app" without specifying stack). This ADR makes the explicit choice: operator = PWA, sharing the Next.js codebase.

ADR-011's per-mode folder structure (`components/transport/manual/`) remains valid — those are dispatcher console components for the Manual operator board / picker / etc. Operator-facing PWA components live in `components/operator/` per this ADR.
