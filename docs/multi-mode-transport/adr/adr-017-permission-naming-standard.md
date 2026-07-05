# ADR-017: Permission Naming Standard + Central Catalog

- **Status**: Accepted
- **Date**: 2026-07-05
- **Deciders**: Solo dev (DTMS)
- **Related**: [ADR-007 Mobile API Authentication](adr-007-mobile-api-authentication.md), [ADR-014 External Auth](adr-014-mobile-api-authentication-external-auth.md)

## Context

DTMS enforces authorization with ~60 permission strings used at 164 `RequirePermission("dtms:…")` call sites across 21 files, seeded into `iam.Permissions` and granted to roles / system-clients. The intended shape was `dtms:<module>:<resource>:<verb>`, but it was never written down or enforced, so drift accumulated:

- **No central catalog** — every code is an inline string literal. A typo (`dtms:trip:raed`) compiles, is never seeded, and silently 403s every user with no compile/test signal.
- **Structure drift** — three modules omit the module segment (`dtms:trip:*`, `dtms:order:*`, `dtms:vehicle:*`); Fleet is internally inconsistent (`dtms:vehicle:write` next to `dtms:fleet:group:write`). Module-level wildcard grants (`dtms:dispatch:*`) are therefore impossible.
- **Noun-in-verb-slot** — `dtms:trip:exception`, `dtms:order:pod`, `dtms:order:bulk`, `dtms:vehicle:maintenance`.
- **Verb drift** — `dtms:planning:order-template:create` vs the `write` used everywhere else; `dtms:planning:consolidate` is only 3 segments.
- **Frontend duplication** — permission strings + the `matches()` wildcard logic are hand-copied into TS with no shared source.

## Decision

### 1. Canonical grammar

```
dtms : <module> : <resource> : <verb>
```

- **Exactly 4 segments** for module-scoped permissions; lowercase; multi-word segments in `kebab-case`.
- **module** = bounded context: `dispatch`, `deliveryorder`, `fleet`, `facility`, `planning`, `iam`, `operator` (the Transport.Manual PWA surface), `reporting` (cross-cutting read/export).
- **resource** = the aggregate/thing acted on (`trip`, `order`, `vehicle`, `map`, `job`, …).
- **verb** = an **action**, never a noun. `read` / `write` for CRUD (`write` = create+update+delete); otherwise a specific action verb (`import`, `submit`, `raise`, `upload`, `instantiate`, `run`, `maintain`, …).
- **Wildcards are grant-side only.** A granted `dtms:dispatch:*` matches every dispatch code by ordinal prefix (`PermissionAuthorizationHandler.Matches`). Renames must preserve `:` boundaries or wildcard grants break.

### 2. Central catalog is the single source of truth

All permission codes are declared once in `DTMS.Iam.Application/Authorization/Permissions.cs` as `PermissionDefinition` (code + description + module). Endpoints reference `Permissions.<Module>.<Name>` — never a raw literal. A guard test rejects any `RequirePermission("dtms:…")` string literal and reconciles the catalog against the seed migrations.

### 3. Deliberate exemption — source-system scheme

`dtms:source:<key>:order:<verb>` (`StandardSystemPermissions.cs`, mirrored in `frontend/lib/iam/standard-system-permissions.ts`) is an external-vendor (OMS/SAP) contract resolved per system-client at runtime. It stays as-is; changing it renegotiates external integrations for no internal gain.

## Reasoning

- **4-segment strict** mirrors Google Cloud IAM / OAuth scope conventions (`namespace:service:resource:verb`) and is what makes module wildcard grants meaningful. The alternative (keep short `trip:`/`order:` resource-level names) was rejected because it permanently forecloses `dtms:<module>:*` grouping and leaves Fleet self-inconsistent.
- **Catalog-first** turns a class of silent runtime 403s into compile errors, and gives one place to change a string (the rename in the follow-up migration flips const values once and every endpoint follows).
- **Verbs-not-nouns** keeps the grammar readable: a code says what action it grants.

## Consequences

### Positive
- ✅ One enforced grammar; typos are compile errors; catalog↔seed drift is a test failure.
- ✅ Module-level wildcard grants (`dtms:dispatch:*`, `dtms:deliveryorder:*`, `dtms:fleet:*`, `dtms:reporting:*`) now work.
- ✅ Single source of truth for code + description + module.

### Negative
- ❌ Renaming 31 codes is a breaking change to stored grants — requires a data migration updating `Permissions`, `RolePermissions`, `SystemClientPermissions`, `PermissionAuditLog`, and any `:*` grant under a renamed prefix, in one transaction (see the rename migration).
- ❌ A ~5-minute stale window (claims cache in `PermissionClaimsTransformer`) after the migration until principals re-resolve.

### Neutral
- 🟡 Frontend keeps a hand-maintained TS mirror + reconciliation test (no codegen pipeline exists); the load-bearing FE gates are all `dtms:iam:*`, unchanged by the rename.

## Rename mapping (summary)

Unchanged (already compliant): Facility, IAM, operator/Transport.Manual, source scheme, `dtms:fleet:group:write`, `dtms:fleet:charging-policy:write`.

| Module | Change |
|---|---|
| Dispatch | `dtms:trip:*` → `dtms:dispatch:trip:*`; `trip:exception` → `dispatch:exception:raise`; `trip:pod` → `dispatch:pod:upload` |
| DeliveryOrder | `dtms:order:*` → `dtms:deliveryorder:order:*`; `order:pod` → `deliveryorder:pod:upload`; `order:item:read` → `deliveryorder:item:read`; `order:bulk` → `order:bulk-submit`; `order:upstream` → `order:create-upstream` |
| Fleet | `dtms:vehicle:*` → `dtms:fleet:vehicle:*`; `vehicle:maintenance` → `fleet:vehicle:maintain` |
| Planning | `planning:consolidate` → `planning:consolidation:run`; `order-template:create` → `order-template:instantiate` |
| Reporting | `report:*` → `reporting:report:*`; `dashboard:read` → `reporting:dashboard:read` |
