# M2-004 lowering v2
Status: todo
Effort: M
Model: Opus, medium effort (extends the M2-003 SSA builder to loops, try regions and heap maps). Sonnet only at high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-003

## Goal
Loops as the CFG presents them with bound metadata; switch; throw and try edges; null shadow variables and NullReferenceException edges; fields and arrays via heap arrays. Extend the oracle generator and the coverage table.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
