# M1-002 ir and interpreter
Status: todo
Effort: L
Depends on: M0-004

## Goal
IR records per VERIFICATION-MODEL section 2 in namespace Equiv.Core.Ir; a well-formedness validator (SSA single assignment, use after def, every block terminated, phi predecessors exist); a deterministic text dump and parser; a test-only interpreter used as an oracle by later tickets. Property tests: random well-formed IR round-trips through dump/parse; the validator rejects each generated violation kind.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
