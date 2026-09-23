---
name: equiv-adr
description: Propose or record an architecture decision for this repo, or decide that one is not needed. Use when a ticket cannot be done as specified, when a ticket contradicts the spec or an accepted ADR, when adding a new dependency category, or when the user asks to change a technology or a component boundary.
---

# Writing an ADR

## Bar test: does this need a new ADR?

An ADR records a decision someone could later want to reverse. Take the first row that fits:

| Situation | Vehicle |
|---|---|
| An existing accepted ADR already decides it, and you are applying it to a case it did not spell out (a cycle, a new construct, an edge) | A dated bullet under `## Clarifications` in that ADR, in the PR that needs it. Flag it in the PR description. |
| The ticket text contradicts or cannot be met under the spec or an accepted ADR, and the spec and ADRs stay as they are | A `Deviation:` line in the ticket's Notes. Correct the ticket text in the same PR and flag it under "Needs your decision" in the PR description. |
| A script knob, a test regex, a temporary state until a later ticket, a process detail | The ticket Notes, or the skill or doc that owns the process. No ADR. |
| It changes a component boundary or a Core contract, adds a dependency category, changes a verdict's meaning, a rule id or the SARIF shape, weakens a gate, or reverses an accepted ADR or a spec row | A new ADR, following the steps below. |

Before writing a new ADR, read `docs/adr/README.md` and grep `docs/adr/` for the topic. If
your draft's Rejected list repeats another ADR's, or its Decision starts with "X stays under
ADR N", it is a clarification, not an ADR. (Proposed ADR 0030 restated ADR 0019 for call
cycles and was folded into 0019's Clarifications.)

## Steps for a new ADR

1. Do not implement the change. ADRs are proposed in their own small PR.
2. Copy this template to `docs/adr/NNNN-<slug>.md` (next number, four digits) and add a
   row to `docs/adr/README.md`:

```
# ADR NNNN: <decision in one line>

Status: proposed (YYYY-MM-DD)      # accepted | superseded by NNNN

## Context
What forced this. Cite the ticket and the observed problem (paste the error, the
benchmark, the missing feature). Max 10 lines.

## Decision
One paragraph, present tense.

## Why
Bullets. Include what was tried.

## Rejected
Each alternative with the one reason it lost.

## Consequences
What becomes easier, what becomes harder, which docs and tickets change.
```

3. Update the affected doc (ARCHITECTURE.md, VERIFICATION-MODEL.md, QUALITY-GATES.md) in
   the same PR only if the user has already accepted the ADR; otherwise leave the docs
   untouched and end your message with the question the user must answer.
4. Never edit an accepted ADR except to set "superseded by", or to append a dated
   `## Clarifications` bullet (`- YYYY-MM-DD (ticket). ...`) that applies the decision to a
   case it did not spell out. A clarification that would change the Decision is a new ADR.
