# M1-005 cli shell and router
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-004

## Goal
System.CommandLine app with the compare command and options from ARCHITECTURE.md; language detection by input extension and contents; rejection paths with exit codes 3 and 4; --dry-run prints the routing decision. Frontend and backend are stub implementations registered through DI so the CLI is testable end to end without Roslyn or Z3.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
