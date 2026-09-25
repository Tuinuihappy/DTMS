---
description: Produce an execution-ready implementation plan in docs/plans/ — recon with cited evidence, forks surfaced as decisions, phases sized with off-ramps. Plans only, never edits source.
argument-hint: <what to build — a feature, a fix, a refactor, or a path/plan doc to continue>
---

# Implementation plan — $ARGUMENTS

You are planning, not building. The deliverable is one markdown file in `docs/plans/`. Do not touch source, migrations, or config in this run.

The plan is good only if a developer can open it cold, three weeks from now, and execute it without re-deriving anything you already worked out. Every hour of recon now is an hour of confused implementation avoided later.

---

## Hard rules

1. **No invented facts.** Every file path, class name, method signature, column, endpoint, env var, and table you name must come from a file you actually read or a grep you actually ran this session. If you didn't verify it, don't write it — put "unverified: <thing>" in Open questions instead.
2. **Cite as you go.** Any load-bearing claim about how the system currently works carries a `path/to/file.cs:123`. Claims without a citation are guesses, and guesses go in Open questions, not in the phases.
3. **Read-only.** Read, Grep, Glob, Bash (read-only commands), and `Explore` subagents. The one file you write is the plan doc. Never run a migration, never start a build to "just check", never run `scripts/wipe-test-data.sql` or anything else that mutates the dev DB.
4. **Plan the whole ask.** If part of it is blocked or a bad idea, plan every other part in full and say plainly in Non-goals what you left out and why. Don't silently shrink the scope — and don't quietly widen it either.
5. **Prefer the existing pattern.** DTMS has a strong house style per module. Before designing anything new, find the two or three closest existing implementations and say in the plan which one you're copying. "New pattern" is a decision that needs a justification line.
6. **Stop and ask only if you must.** If two readings of the request would produce materially different plans, ask up to 3 focused questions before Stage 1 and wait. Otherwise pick the sensible reading, state it as an assumption at the top of the plan, and keep going.

---

## Stage 0 — Frame (a few minutes)

Write, for yourself, before any tool call:

- The ask in **one sentence**, in terms of observable behaviour ("an operator can …", "the reconciler stops …"), not in terms of code.
- Rough size: **S** (< 4h, one sitting) · **M** (1–2 days, 2–4 phases) · **L** (> 2 days, 4+ phases, needs off-ramps and a cutover section).
- Its **kind**, which decides where the plan's weight goes:

| Kind | The sections that carry the weight | What you can thin out |
|---|---|---|
| **Feature** | Decision table, phased steps, per-phase acceptance | Root cause |
| **Bug fix** | **Root cause + a reproduction + why the tests didn't catch it**, the minimal fix, the regression test that would have failed | Usually one phase; no decision table |
| **Refactor** | The behaviour-preservation argument, the test that proves before == after, a mechanical step list | Goal metrics |
| **Integration** | Both sides' contract, retry/idempotency semantics, what happens when the far side is down or slow | — |
| **Migration / data** | expand → backfill → contract ordering, row counts before and after, rollback | Frontend sections |
| **Ops** | The signal — metric, log line, alert — that will tell you it works in production next week | Decision table |

A bug-fix plan that opens with a decision table, or a feature plan with no acceptance checks, has put its effort in the wrong place.

Then: does the ask name a file, plan doc, or phase in `docs/plans/` or `docs/`? Read it first — you may be continuing an existing plan, and continuing beats starting over.

**Recon budget**: S ~10 min · M ~30 min · L ~1h. If you blow through it and still can't answer the six questions below, the ask is too big for one plan. Stop and propose splitting it into 2–3 plan docs with a parent index — a 40-hour plan doc is a document nobody executes.

---

## Stage 1 — Recon (the part that decides the plan's quality)

Fan out. Launch `Explore` subagents in parallel for independent questions — one per question, not one for everything — and grep directly for things you can name exactly.

Six things. Do not proceed until you can answer all six with citations.

| # | Question | How you know you're done |
|---|---|---|
| 1 | **Where does this live?** Which module(s) under `src/Modules/`, which `DTMS.Api` endpoint file, which `frontend/app/` route | You can name every file that will change, and its current line count |
| 2 | **What's the closest existing thing?** The feature already shipped that most resembles this one | You can point at it and say "same shape, differences are X and Y" |
| 3 | **What crosses a boundary?** Another module, an outbox event, a projection, a callback contract, an external system (OMS / RIOT3 / WMS) | You've read the contract on both sides, not just ours |
| 4 | **What's the data shape?** New columns, tables, indexes, and whether existing rows need backfilling | You've read the current entity + `DbContext` config + the last migration that touched it |
| 5 | **What already tried this?** Search `docs/`, `docs/plans/`, and `git log` for prior attempts, reversals, and deliberate non-decisions | You know whether this was tried and abandoned, and why |
| 6 | **What breaks if I'm wrong?** The blast radius — which live flow degrades, which rows go bad, what the operator sees | You can write a one-line failure story |

Useful probes:

```bash
git log --oneline -40 -- src/Modules/<Module>       # what has been happening here
git log -S"<symbol>" --oneline -20                  # when was this introduced/removed
git show --stat <closest past commit>               # ← your estimate anchor, see below
ls -1 src/Modules/<Module>/Migrations | tail -5     # latest MigrationId timestamps
grep -rn "<endpoint path>" src/DTMS.Api frontend/   # who calls this
```

**Point 5 is the one people skip and the one that most often saves a day.** This repo has a history of features built and then deliberately withdrawn. Re-proposing one of them is the single most expensive failure mode here. If the thing you're about to plan was removed on purpose, say so at the top of the plan and stop for a decision.

**Estimate anchor (do this, don't skip it).** Once you've found the closest past change in point 2, run `git show --stat` on it. A change that touched 9 files and 400 lines took a real person a real day. If your plan claims 3h for something of the same shape, your estimate is wrong, not their day. Cite the anchor commit in the plan.

---

## Stage 2 — Surface the decisions

List every fork in the road *before* writing phases. For each: the options, the one you'd pick, and — this is the useful column — the condition under which the other option wins. A plan that hides its forks makes the implementer re-open them mid-flight.

Typical forks here: new module vs. extend existing · new table vs. new column · sync call vs. outbox event · new endpoint vs. new query param · projection vs. live read · backfill vs. leave history alone · frontend polls vs. SignalR pushes · feature flag vs. ship direct.

If a fork genuinely needs the user's input (a product decision, an external contract, a cost trade), do **not** silently default it. Put it in Open questions at the top of the plan and mark the affected phase as blocked on it.

---

## Stage 3 — Write the plan

Write to `docs/plans/<kebab-slug>.md`. Match the house style — read [`docs/plans/g1-signalr-drain-protocol.md`](../../docs/plans/g1-signalr-drain-protocol.md) once if you haven't; it is the reference shape.

````markdown
# <Short name> — implementation plan

> **Status**: 🔵 Not started — drafted <YYYY-MM-DD>
> **Ask**: <the one-sentence ask from Stage 0>
> **Kind / size**: <feature | bug fix | refactor | integration | migration | ops> · <S|M|L>, ~Nh
> **Parent**: <link to the plan/doc this descends from, or "none">
> **Assumptions**: <the readings you picked where the ask was ambiguous — or "none">

## The change in one sentence

<What will be true after this ships that isn't true now. Behaviour, not code.>

## Goal / non-goal

**Goal**: <the observable outcome, with the number or condition that makes it true>

**Non-goal**: <what this deliberately does not do, and why — including anything from the ask you are not planning>

---

## Pre-flight (~N min)

Read once before starting:

- [`path/to/file.cs`](../../path/to/file.cs) — <why this file, in one clause>
- <4–7 entries; the files the implementer must have in their head, not every file that changes>

Decision check before coding (getting these wrong wastes <N>h):

| Decision | Default | When to pick the alternative |
|---|---|---|
| <fork> | <the pick> | <the condition that flips it> |

Open questions (blocking the phase named):

- [ ] <question> — blocks Phase N. <what you'd do if forced to guess>

**Estimate anchor**: `<sha>` — <what it did>, <N> files / <N> lines. This plan is <bigger/smaller/the same> shape.

---

## Phase 1 — <name> (~Nh)

### 1.1 <step> (~Nh)

Files:

- New: `path/to/NewThing.cs`
- Edit: `path/to/Existing.cs` — <the specific change, e.g. "add the `IJobReader` dependency and the branch at line 84">
- Tests: `tests/Modules/<Module>.UnitTests/<Thing>Tests.cs` — <the case that fails before this change and passes after>

Shape:

```csharp
// The signature, the interface, the migration SQL, the component props.
// Enough that the implementer types it in, not designs it. Not a full body.
```

<Any gotcha specific to this step — a concurrency token, an Ignore(), a param collision.>

### 1.2 <step> (~Nh)

…

### Phase 1 acceptance

- <a check that can actually be run, with the command that runs it>

**Commit**: `feat(<scope>): <what a person can now do, in plain words>`

---

## Phase 2 — …

<Phases are ordered so each one leaves the repo in a committable, working state. If a phase can't, say so explicitly and name the phase it must land with.>

---

## Verification

| What | How (exact command or click path) | Where |
|---|---|---|
| <behaviour> | `dotnet test tests/Modules/<M>.UnitTests -m:1` | <test project> |
| <in production> | <the metric, log line, or dashboard panel> | <docs/grafana or the log query> |

## Docs to update when this lands

- `docs/<x>.md` — <what changes; if this plan has a Parent, mark its item ✅ there>
- New ADR needed? <yes — which decision / no — why not>. ADRs live in `docs/multi-mode-transport/adr/`, numbered sequentially.

## Definition of done

- [ ] <checkable outcome, not "code written">
- [ ] <the number that proves it, where there is one>
- [ ] Effort total stayed within ~Nh (overshoot by > 50% = pause and re-scope)

## Off-ramps

- **If <step> takes > <N>h**: <the 80% version to ship instead>
- **If <discovery>**: <the cheaper path>
- **If blocked on <external>**: <what ships without it, and what it's worth>

## Risks & rollback

| Risk | Likelihood | Mitigation / rollback |
|---|---|---|
| <risk> | Low/Med/High | <how you undo it — name the migration, the flag, the revert> |

## What would invalidate this plan

- <the assumption that, if false, means re-planning rather than adapting>

## When this plan is picked up

**First move**: open `<file>` and <the literal first edit>.

<2–3 sentences: what the pre-flight covers, realistic cold-start-to-done estimate.>
````

**Sizing.** A `### N.N` step is ≤ 2h — bigger means it's two steps. A phase is ≤ 6h, one sitting. Estimates are honest, not optimistic; an S-sized ask gets one phase, no risks table, no off-ramps, and forcing it into the full template is its own kind of failure.

**Tests live inside the phase that creates the behaviour.** A trailing "Phase N — Tests" is a smell: it is the phase that gets dropped the moment the estimate slips. Every step that changes behaviour names its test file and the case that would fail without the change.

**Commit subjects are part of the plan.** Every phase ends with the commit it produces, in house style: `type(scope): <what a person can now do or no longer suffers>`, lowercase, no trailing period, behaviour rather than mechanics — `fix(dispatch): stop the trip drawer reporting an order status from the past`, not `fix: update trip drawer`. Scopes in use: `api`, `dispatch`, `fleet`, `frontend`, `operator`, `orders`, `ops`, `projections`, `scan`, `docs`, `tests`. Writing this line forces you to state the phase's outcome in one sentence — if you can't, the phase isn't coherent yet.

---

## DTMS landmines — walk this list explicitly

For each row the plan touches, the plan must say how it's handled. For rows it doesn't touch, say nothing — but you must have checked.

**Data / EF**

- `dotnet-ef` does not work on .NET 10 preview. **Migrations are written by hand** — the plan includes the migration class, not "run `dotnet ef migrations add`".
- All 8 DbContexts share `public.__EFMigrationsHistory`. A new migration needs a **unique MigrationId timestamp** (offset by seconds from any sibling) or one silently never runs.
- A new `OutboxMessage` property needs `b.Ignore()` in **all 5 per-module DbContexts** or the INSERT fails 42703 and surfaces as "Upstream error 500".
- DbContexts have `EnableRetryOnFailure`. An explicit `BeginTransactionAsync` **must** be wrapped in `CreateExecutionStrategy().ExecuteAsync`.
- `FromSqlInterpolated` collides with EF's own `LIMIT @p0` on page ≥ 2 (42804). Use `FromSqlRaw` + named `NpgsqlParameter`.
- Owned-entity properties need explicit `HasColumnName` or you get `CargoSpecific_X` columns.
- A non-null column on a populated table is **three steps, not one**: expand (add nullable) → backfill (a script in `scripts/`, named like the existing `backfill-<phase>-<thing>.sql`) → contract (set non-null). Say which phase does which, and how you count rows before and after.
- Don't denormalize a mutable cross-module status into another module's table — read it live through its reader interface.

**Projections / events**

- `ProjectionFaults` is the one transient-retry policy. `OrderListView` / `OrderFacts` / `TripFacts` / `JobFacts` use check-then-insert **on purpose** (row + inbox share one `SaveChanges`) — don't "fix" them into upserts.
- `Trip` carries an `xmin` concurrency token. New writers handle conflicts with the ResetTracking → reload → reapply pattern.

**Permissions / API surface**

- Permission codes live in the `Permissions` catalog (`DTMS.Iam.Application.Authorization`) and are **never raw literals at the endpoint**. Grammar is strict — `dtms:<module>:<resource>:<verb>`, four lowercase kebab segments, module from the allowed list. [`tests/DTMS.ArchitectureTests/PermissionCatalogTests.cs`](../../tests/DTMS.ArchitectureTests/PermissionCatalogTests.cs) fails the build otherwise (ADR-017).
- A brand-new `dtms:*` code works instantly for this user's own account (wildcard grant) — don't write "grant the permission on the auth service" as a blocking first step.
- All callers share one 100/min per-IP rate-limit bucket via Next's IP.

**Packages**

- Central Package Management is on ([`Directory.Packages.props`](../../Directory.Packages.props)). A new NuGet package is a `PackageVersion` line **there** plus a **versionless** `PackageReference` in the csproj. A version on the `PackageReference` breaks the build.

**Tests**

- Name the project, never "add tests": `tests/Modules/<Module>.{Unit,Integration}Tests` (13 of them), plus `DTMS.Api.UnitTests`, `DTMS.ArchitectureTests`, `DTMS.FunctionalTests`.
- The integration projects are largely stubs. If a step says "add an integration test", check whether the harness it needs actually exists — if not, building that harness is its own sized step, or an off-ramp down to a unit test.
- Backend query/EF changes are verified through a scratchpad DbContext harness against `localhost:5434`, not through an HTTP call (LDAP-only, no `/api/auth/token`).

**Frontend**

- `d:\DTMS\frontend`, Next 16 + shadcn **base-ui variant** — `render`, not `asChild`.
- **No eslint.** `npm run lint` is a dead script. Verify with `npm run typecheck` and hand-check dependency arrays yourself.
- Overlay backdrops use the `OverlayBackdrop` primitive, not `AnimatePresence`.
- `proxy.ts` is an explicit prefix allowlist with a prebuild guard (`npm run check:routes`) — a new API route prefix needs a line there, and must never match bare `/api`.

**Process**

- Solo developer. No PR flow, no reviewer, no team coordination step. "Review" means a short self-check.
- **Never create a new branch** — plan onto `main` or the current feature branch.
- Build the solution with `-m:1`; it's `DTMS.slnx`, not `.sln`.
- Never plan a step that wipes or truncates the dev DB to prove something works.

**Containers (podman, not Docker Desktop)**

- Docker Desktop is gone and is not coming back. Rebuild the stack with `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 [-Only api|frontend]` — **never** `docker-compose build` (no buildx; podman's compat API drops the stream and can kill the rootful socket).
- Podman here **cannot publish container ports** (netavark/nftables on the WSL kernel). `localhost:3000` / `:5219` / `:5434` work only through host networking plus the SSH tunnels an autostart supervisor maintains. A plan step that exposes a new port or a new service must say how it becomes reachable — don't assume `ports:` in a compose file does anything.

---

## Stage 4 — Self-audit before you hand it over

Re-read your own plan and answer these honestly. Fix what fails; don't just note it.

1. Is there a **file path or symbol in here I did not verify**? → remove it or move it to Open questions.
2. Could someone execute Phase 1 **without asking me a single question**? → if not, the missing answer belongs in the plan.
3. Does every phase end with the repo **committable and working**? → if not, say which phases are joined at the hip.
4. Are the estimates ones I'd **defend out loud**, against the anchor commit's diffstat? → if a step is "~1h" because that sounded nice, re-estimate.
5. Are the off-ramps **real** — does each one name a smaller shippable thing, not "try harder"?
6. Did I check whether this was **already built and deliberately removed**?
7. Is there a step that is really **a decision in disguise** ("figure out how X works", "investigate Y")? → do that investigation now, or promote it to a named spike with a time box.
8. Does Verification contain **commands I could paste**, or vague intentions?
9. **Did I add anything the user didn't ask for?** → cut it, or move it to a "Later, not now" list at the bottom. A plan that quietly grows the ask is how a two-day job becomes a week.
10. Can I name **the literal first file and first edit**? → if not, Phase 1.1 is still a wish, not a step.

---

## Finish

Post in chat, in under 15 lines:

- the path to the plan doc
- the one-sentence change
- total estimate, phase count, and the anchor commit you sized against
- the 1–3 decisions you defaulted, and the single riskiest assumption
- any Open questions that block a phase

Then stop and ask whether to start Phase 1. Do not begin implementing.
