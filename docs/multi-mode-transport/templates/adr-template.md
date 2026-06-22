# ADR-{NNN}: {Short Decision Title}

<!--
INSTRUCTIONS (delete before committing):

1. Filename convention: adr-{NNN}-{kebab-case-title}.md
   - NNN = next available 3-digit number (zero-padded). Check existing ADRs to pick.
   - Title should be a noun phrase, not a question: "Multi-Mode Transport Module Split" not "Should we split modules?"

2. Status values:
   - Proposed: under discussion, not yet adopted
   - Accepted: decided, in effect
   - Deprecated: no longer relevant but kept for history
   - Superseded by ADR-XXX: explicitly replaced (add the superseding ADR's number)

3. Each section below has guidance comments — read them and DELETE before committing.

4. Length target: 200-500 lines. Longer is fine if complex; shorter (under 100 lines) usually means you're missing alternatives or edge cases.

5. After writing:
   - Add the new ADR to docs/multi-mode-transport/README.md (file tree + summary table + reading order if role-relevant)
   - Cross-link FROM existing ADRs that this one relates to (their "Related ADRs" sections)
   - Cross-link TO existing ADRs in your "Related ADRs" section

6. Immutability rule:
   - Once Accepted, do NOT edit the Decision section to reverse it. Write a new ADR that supersedes this one.
   - Typo fixes, link updates, and clarifications ARE allowed (the spirit of the decision must stay intact).
-->

- **Status**: Proposed | Accepted | Deprecated | Superseded by ADR-XXX
- **Date**: YYYY-MM-DD
- **Deciders**: {Team or specific names}
- **Related**: [ADR-XXX](adr-xxx-slug.md), [ADR-YYY](adr-yyy-slug.md)

## Context

<!--
Describe the situation that requires a decision. Include:
- What problem are we solving? (not what solution we want)
- What constraints exist? (technical, business, team, time)
- What's the current state? (link to files / existing code with line numbers if helpful)
- What's at stake if we get this wrong?

Good context section answers: "Why are we even having this conversation?"
A reader 6 months from now should understand the motivation without asking questions.

End this section with 3-7 specific questions that the Decision answers.
-->

{Describe the problem + constraints. Reference specific files where relevant — use markdown links to repo files.}

Existing infrastructure:
- {Bullet relevant existing code/infra/decisions}
- {Use [file links](../../../path/to/file.cs:42) with line numbers when discussing specific code}

Requirements:
1. **{Requirement 1}** — {brief why}
2. **{Requirement 2}** — {brief why}
3. ...

ปัญหาที่ต้องตัดสิน:
1. {Specific question 1?}
2. {Specific question 2?}
3. ...

## Decision

<!--
State the chosen approach clearly. Use "We will..." or "ใช้..." phrasing.
Be specific — name files, classes, interfaces, libraries, config keys.
Include code snippets showing the canonical pattern.

If the decision has multiple parts, structure as sub-headings (### Convention 1, ### Convention 2)
or numbered list (### Decision A, ### Decision B).

Avoid burying the actual decision under prose. A reader scanning should see WHAT was chosen in the first 5 lines.
-->

ใช้ **{primary choice}** {with key qualifier or trade-off statement}:

```csharp
// Canonical code pattern showing the decision in action
public interface I{ExampleInterface}
{
    {ReturnType} {MethodName}({Parameters});
}
```

### {Sub-decision 1 — if applicable}

{Details + code if needed}

### {Sub-decision 2 — if applicable}

{Details}

## Alternatives Considered

<!--
List the SERIOUS alternatives you considered and rejected. Minimum 2, usually 3-5.

For each:
- **Name** the alternative clearly
- **Pros**: real benefits (not strawmen)
- **Cons**: real downsides
- **Rejected because**: the specific reason — must be honest, not face-saving

Skip trivial alternatives (e.g. "we could not do anything").
Include alternatives that other engineers would reasonably propose.

If you're tempted to write "we chose X because it's the standard" — find the alternative someone would push for and explain why it's worse here specifically.
-->

### Alternative A: {Name}

{1-3 sentence description}

**Pros:**
- {Pro 1}
- {Pro 2}

**Cons:**
- {Con 1}
- {Con 2}

**Rejected because:** {Specific reason — usually one of: complexity not worth benefit, conflicts with another ADR, team experience gap, cost, lock-in}

### Alternative B: {Name}

{...}

**Rejected because:** {...}

### Alternative C: {Name}

{...}

**Rejected because:** {...}

## Implementation Details

<!--
Concrete code showing HOW the decision is realized. This section bridges Decision (what) to actual code.

Include:
- DI registration pattern
- Config structure (if applicable)
- Key interface signatures
- Database schema (if applicable)
- Cross-cutting concerns (logging, error handling, retry)

This is where future engineers look when they say "ok, but show me how it works."
-->

### {Section 1 — e.g. DI Registration / Config / Schema}

```csharp
// Code showing the pattern
```

### {Section 2}

```json
// Config example if relevant
```

### {Section 3}

{...}

## Edge Cases & Failure Modes

<!--
List 3-7 edge cases or failure scenarios that the decision must handle.

For each:
- Describe the scenario
- Explain how it's handled (or note "not handled — out of scope")
- Code snippet if non-obvious

This section catches "what about X?" objections before they derail review.
Common categories:
- Concurrency / race conditions
- Partial failure (network, DB)
- Data inconsistency
- Config misconfiguration
- Adversarial input
- Migration / rollback issues
-->

### Edge Case 1: {Scenario name}

Scenario: {describe situation}

**Handling:**
- {How it's handled}
- {Mitigation step}

### Edge Case 2: {Scenario name}

{...}

### Edge Case 3: {Scenario name}

{...}

## Consequences

<!--
Honest accounting of what changes after this decision.

Positive: real benefits we get
Negative: real costs we pay (don't minimize — future you will appreciate honesty)
Neutral: things that change but aren't clearly good or bad

Aim for 3-5 items per category. If Negative is empty, you're not being honest.
-->

### Positive

- ✓ {Benefit 1}
- ✓ {Benefit 2}
- ✓ {Benefit 3}

### Negative

- ✗ {Cost / downside 1}
- ✗ {Cost / downside 2}
- ✗ {Cost / downside 3}

### Neutral

- {Change that's not clearly good or bad 1}
- {Change 2}

## Acceptance Criteria

<!--
Checklist of what "done" looks like. Each item should be verifiable.

These become the merge gate for the PR that implements this ADR.
-->

- [ ] {Specific implementation milestone 1}
- [ ] {Test coverage milestone}
- [ ] {Documentation milestone}
- [ ] {Operational readiness milestone}

## Related ADRs

<!--
Cross-reference other ADRs:
- Parent/child relationships (this decision derives from / drives another)
- Sibling decisions on same axis (separate but coordinated)
- Superseded / superseding ADRs

After writing, remember to add backlinks: each ADR you mention should have THIS ADR in its Related list too.
-->

- [ADR-XXX](adr-xxx-slug.md) — {1-line description of relationship}
- [ADR-YYY](adr-yyy-slug.md) — {1-line description of relationship}

## References

<!--
External references that justify or inform the decision:
- Standards / specs (RFC, OGC, W3C)
- Library docs
- Blog posts / talks (with stable URLs)
- Internal documents (memory entries, plan files)
- Codebase references (specific files this decision touches)
-->

- {External link 1}: {URL}
- {External link 2}: {URL}
- [Memory: {memory-name}](../../memory/{memory-name}.md)
- Existing code: [file.cs](../../../src/path/to/file.cs)
