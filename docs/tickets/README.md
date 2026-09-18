# Tickets

One file per ticket, named `M<n>-<nnn>-<slug>.md`. Status is the first line after the
title: `Status: todo | in-progress | done (PR #n)`. Agents update it in the PR.

Template:

```
# M1-002 IR types, interpreter, dump format
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
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

The `Model:` line is mandatory and is checked first by the task-loop skill. It names
the least capable model family and effort the ticket was planned for. An agent of a
weaker family than named, or of the named family below the named effort, must stop
before reading further and ask the user to switch; it must not "give it a try".
Effort L tickets are the design-heavy ones and carry a Design section with the
intended algorithm, types and pitfalls; S and M tickets are sized for Sonnet.

Tickets leave implementation details open on purpose. An agent settles them with the
`equiv-decide` skill and a `Decision:` line in Notes; it does not ask.

M0 and every L ticket are written in full. The remaining S and M tickets are stubs
(goal + generic deliverables) that the agent picking them up expands per the template,
before writing code, in the same PR.
