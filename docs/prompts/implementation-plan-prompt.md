# Implementation-plan prompt (portable)

The project-agnostic twin of [`.claude/commands/plan.md`](../../.claude/commands/plan.md). Paste the block below into any Claude session — Claude Code in another repo, or claude.ai with files attached — after filling the slots at the top. Drop it into a new repo as `.claude/commands/plan.md` (add the frontmatter from the DTMS copy) and it becomes a `/plan` command there.

The only part you must fill in is `<project-context>`. Everything it should contain is described in the checklist at the bottom of this file — that section is where a generic plan prompt becomes a good one, so don't leave it empty.

---

```markdown
# Implementation plan — <THE ASK>

You are planning, not building. The deliverable is one markdown plan document. Do not modify source, migrations, config, or infrastructure in this run.

The plan is good only if a developer can open it cold, three weeks from now, and execute it without re-deriving anything you already worked out.

<project-context>
Stack:
Repo layout:
Where plans live (+ the one best existing plan to copy the shape from):
Build / test / typecheck commands:
Test project map (which kind of test goes where):
Known landmines (things that have bitten us before):
Commit message style (with two real examples):
Process constraints (branching, review, deploy, what may touch a shared DB):
</project-context>

## Hard rules

1. **No invented facts.** Every file path, symbol, signature, column, endpoint, and table you name must come from a file you actually read or a search you actually ran. Unverified things go in "Open questions", never in a phase.
2. **Cite as you go.** Load-bearing claims about current behaviour carry a `path/to/file:line`. Uncited claims are guesses.
3. **Read-only.** Search and read freely. The one file you write is the plan.
4. **Plan the whole ask.** If part is blocked or unwise, plan the rest in full and say in Non-goals what you left out and why. Don't silently shrink the scope — and don't quietly widen it either.
5. **Prefer the existing pattern.** Find the two or three closest existing implementations first and say which one you're copying. Introducing a new pattern is a decision that needs a justification line.
6. **Ask only when it changes the plan.** If two readings of the ask produce materially different plans, ask up to 3 focused questions and wait. Otherwise pick the sensible reading, record it as an assumption, and continue.

## Stage 0 — Frame

Before any tool call, settle:

- The ask in **one sentence**, as observable behaviour, not as code.
- Its size — **S** (< 4h, one sitting) · **M** (1–2 days, 2–4 phases) · **L** (> 2 days, needs off-ramps and a cutover section).
- Its **kind**, which decides where the plan's weight goes:

| Kind | The sections that carry the weight | What you can thin out |
|---|---|---|
| **Feature** | Decision table, phased steps, per-phase acceptance | Root cause |
| **Bug fix** | **Root cause + a reproduction + why the tests didn't catch it**, the minimal fix, the regression test that would have failed | Usually one phase; no decision table |
| **Refactor** | The behaviour-preservation argument, the test that proves before == after, a mechanical step list | Goal metrics |
| **Integration** | Both sides' contract, retry/idempotency semantics, what happens when the far side is down or slow | — |
| **Migration / data** | expand → backfill → contract ordering, row counts before and after, rollback | UI sections |
| **Ops** | The signal — metric, log line, alert — that will tell you it works in production next week | Decision table |

A bug-fix plan that opens with a decision table, or a feature plan with no acceptance checks, has put its effort in the wrong place.

If the ask names an existing doc, ticket, or plan, read it first: you may be continuing something rather than starting it.

**Recon budget**: S ~10 min · M ~30 min · L ~1h. If you blow through it and still can't answer the six questions below, the ask is too big for one plan. Stop and propose splitting it into 2–3 plan docs with a parent index — a 40-hour plan doc is a document nobody executes.

## Stage 1 — Recon

Fan out — parallel searches, one per question. Don't proceed until all six are answered **with citations**:

1. **Where does this live?** Every file that will change, and its current shape.
2. **What's the closest existing thing?** The shipped feature this most resembles — "same shape, differences are X and Y".
3. **What crosses a boundary?** Another module, a queue, a schema, an external API. Read the contract on *both* sides.
4. **What's the data shape?** New columns, tables, indexes, and whether existing rows need backfilling.
5. **What already tried this?** Search docs and `git log --oneline -40 -- <path>` / `git log -S"<symbol>"` for prior attempts and deliberate reversals.
6. **What breaks if I'm wrong?** The blast radius, as a one-line failure story.

Point 5 is the most-skipped and highest-value. If the thing you're about to plan was built and removed on purpose, say so at the top and stop for a decision.

**Estimate anchor (do this, don't skip it).** Once you've found the closest past change in point 2, run `git show --stat` on it. A change that touched 9 files and 400 lines took a real person a real day. If your plan claims 3h for something of the same shape, your estimate is wrong, not their day. Cite the anchor commit in the plan.

## Stage 2 — Surface the decisions

List every fork *before* writing phases: the options, your pick, and the condition under which the other option wins. A plan that hides its forks makes the implementer re-open them mid-flight.

Common forks: new module vs. extend existing · new table vs. new column · synchronous call vs. queued event · new endpoint vs. new parameter · cached/projected vs. live read · backfill vs. leave history · push vs. poll · feature flag vs. ship direct.

Forks that need a human (product calls, external contracts, cost trades) go in **Open questions** with the blocked phase named — never silently defaulted.

## Stage 3 — Write the plan

Write to `<plans directory>/<kebab-slug>.md`:

    # <Short name> — implementation plan

    > **Status**: 🔵 Not started — drafted <YYYY-MM-DD>
    > **Ask**: <one sentence>
    > **Kind / size**: <kind> · <S|M|L>, ~Nh
    > **Assumptions**: <readings picked where the ask was ambiguous, or "none">

    ## The change in one sentence
    <What will be true after this ships that isn't true now.>

    ## Goal / non-goal
    **Goal**: <observable outcome, with the number or condition that makes it true>
    **Non-goal**: <what this deliberately doesn't do, and why>

    ## Pre-flight (~N min)
    Read once before starting:
    - `path/to/file` — <why, in one clause>   ← 4–7 entries, the ones to hold in your head

    Decision check before coding:
    | Decision | Default | When to pick the alternative |

    Open questions (blocking the phase named):
    - [ ] <question> — blocks Phase N. <what you'd do if forced to guess>

    **Estimate anchor**: `<sha>` — <what it did>, <N> files / <N> lines.

    ## Phase 1 — <name> (~Nh)
    ### 1.1 <step> (~Nh)
    Files:
    - New: `path`
    - Edit: `path` — <the specific change>
    - Tests: `path` — <the case that fails before this change and passes after>

    Shape:
    ```
    <signature / schema / props — enough to type in, not a full body>
    ```
    <gotcha specific to this step>

    ### Phase 1 acceptance
    - <a check that can actually be run, with the command that runs it>

    **Commit**: `<type>(<scope>): <what a person can now do, in plain words>`

    ## Verification
    | What | How (exact command) | Where |
    | <in production> | <the metric, log line, or dashboard panel> | <where it lands> |

    ## Docs to update when this lands
    - `docs/<x>.md` — <what changes; if this plan has a Parent, mark its item ✅ there>
    - New ADR needed? <yes — which decision / no — why not>

    ## Definition of done
    - [ ] <checkable outcome, not "code written">
    - [ ] Effort stayed within ~Nh (overshoot by > 50% = pause and re-scope)

    ## Off-ramps
    - **If <step> exceeds <N>h**: <the 80% version to ship instead>
    - **If blocked on <external>**: <what ships without it, and what it's worth>

    ## Risks & rollback
    | Risk | Likelihood | Mitigation / rollback |

    ## What would invalidate this plan
    - <the assumption that, if false, means re-planning rather than adapting>

    ## When this plan is picked up
    **First move**: open `<file>` and <the literal first edit>.
    <2–3 sentences: realistic cold-start-to-done estimate.>

**Sizing.** A step is ≤ 2h (bigger = two steps); a phase is ≤ 6h (one sitting). Each phase must leave the repo committable and working — if one can't, say which phase it must land with. Estimates are honest, not optimistic. An S-sized ask gets one phase, no risks table, no off-ramps; forcing it into the full template is its own failure.

**Tests live inside the phase that creates the behaviour.** A trailing "Phase N — Tests" is a smell: it is the phase that gets dropped the moment the estimate slips. Every step that changes behaviour names its test file and the case that would fail without the change. Before planning a test at a level this repo doesn't really have, check that the harness exists — building it is its own sized step, or an off-ramp to a cheaper level.

**Commit subjects are part of the plan.** Every phase ends with the commit it produces, in the project's own style. Writing that line forces you to state the phase's outcome in one sentence — if you can't, the phase isn't coherent yet.

## Stage 4 — Self-audit

Re-read your own plan and fix what fails:

1. Any path or symbol **I did not verify**? → remove it, or move it to Open questions.
2. Could someone execute Phase 1 **without asking me anything**?
3. Does every phase end **committable and working**?
4. Are the estimates ones I'd **defend out loud**, against the anchor commit's diffstat?
5. Is each off-ramp a **real smaller shippable thing**, not "try harder"?
6. Did I check whether this was **already built and deliberately removed**?
7. Is any step **a decision in disguise** ("investigate X")? → do it now, or promote it to a time-boxed spike.
8. Does Verification hold **commands I could paste**?
9. **Did I add anything the user didn't ask for?** → cut it, or move it to a "Later, not now" list at the bottom.
10. Can I name **the literal first file and first edit**? → if not, Phase 1.1 is still a wish, not a step.

## Finish

Post in chat, under 15 lines: the plan's path · the one-sentence change · total estimate, phase count and the anchor commit you sized against · the 1–3 decisions you defaulted and the riskiest assumption · any Open questions blocking a phase.

Then stop and ask whether to start Phase 1. Do not begin implementing.
```

---

## Filling in `<project-context>`

This is the slot that separates a plan prompt producing generic advice from one producing a plan someone can execute.

**Stack** — languages, frameworks and versions, but only where a version actually constrains the plan (a preview runtime, a major with breaking changes, a UI library variant with a different API).

**Repo layout** — the four or five directories a plan would ever name, one clause each. Not a tree dump.

**Where plans live** — the directory, plus a link to the single best existing plan to copy the shape from. One good example does more than a page of format instructions.

**Build / test / typecheck commands** — the exact invocations, including any flag that isn't the default (parallelism limits, a non-standard solution file, a test filter). These end up in the Verification table verbatim.

**Test project map** — which project holds unit vs. integration vs. contract tests, and honestly which of them are real versus stubs. Without this, plans say "add tests" and the implementer has to decide where; with it, every step names a file. Flag any level whose harness doesn't exist yet, so the plan doesn't casually budget an afternoon for it.

**Known landmines** — the highest-value section, and the one only you can write. Each entry is a rule that cost real time to learn: the code generator that doesn't run on this runtime, the config that must change in five places at once, the retry policy that breaks explicit transactions, the registry a new entry must be added to or the build fails, the package manifest that owns all versions centrally, the lint that isn't installed so its class of bug goes uncaught. Write them as *"do this / it fails like that"*, not as background. If you keep notes or agent memory, harvest them here — a landmine list is the whole reason a project-specific plan prompt beats a generic one.

**Commit message style** — the convention plus two real examples pasted from `git log --oneline -20`. Examples teach it in a way a rule never does.

**Process constraints** — branching rules, whether review exists, whether anything may touch a shared or production database, deploy gates. These decide what a phase is even allowed to contain.
