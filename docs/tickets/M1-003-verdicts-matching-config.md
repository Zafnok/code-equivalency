# M1-003 verdicts matching config
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-002

## Goal
Verdict types; ProcedureIdentity and its normalisation (namespace map, generic arity, parameter types); IProcedureMatcher producing pairs plus Added and Removed; equiv.config.json schema (rename maps, call identity maps, bound, timeout) with validation and helpful errors. Property test: matching is symmetric and total over generated identity sets.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
