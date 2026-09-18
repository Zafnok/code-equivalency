---
name: equiv-adr
description: Propose or record an architecture decision for this repo. Use when a ticket cannot be done as specified, when adding a new dependency category, or when the user asks to change a technology or a component boundary.
---

# Writing an ADR

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
4. Never edit an accepted ADR except to set "superseded by".
