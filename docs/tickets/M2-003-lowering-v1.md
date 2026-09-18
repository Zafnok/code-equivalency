# M2-003 lowering v1
Status: todo
Effort: L
Depends on: M2-002

## Goal
CFG to IR for straight-line code, if/else, integer arithmetic (checked and unchecked), bool logic, returns, opaque calls, and the IrOpaque fallback for every other OperationKind. Start the IOPERATION-COVERAGE.md table. Lowering-oracle property test per VERIFICATION-MODEL section 7 using Roslyn scripting as the reference executor.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
