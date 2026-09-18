# Tickets

One file per ticket, named `M<n>-<nnn>-<slug>.md`. Status is the first line after the
title: `Status: todo | in-progress | done (PR #n)`. Agents update it in the PR.

Template:

```
# M1-002 IR types, interpreter, dump format
Status: todo
Effort: L
Depends on: M0-003

## Goal
One paragraph. What exists when this is done.

## Spec references
VERIFICATION-MODEL.md section 2, section 7

## Deliverables
- [ ] concrete file / type / behaviour
- [ ] tests: which kinds (unit / property / snapshot / integration)

## Out of scope
What a tempted agent must not do here.

## Notes
Left empty by the author; the implementer records surprises.
```

Only the M0 tickets are written in full. M1 to M3 tickets are stubs (goal + generic
deliverables) that the agent picking them up expands per the template, before writing
code, in the same PR.
