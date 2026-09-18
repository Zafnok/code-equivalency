# M1-004 sarif and baseline
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-003

## Goal
Sarif.Sdk writer producing the section 6 mapping with rule metadata for EQ001 to EQ005; result fingerprint and baselineState computation; IReportSink with a file implementation. Snapshot tests for each verdict kind; a test that validates output with the SARIF SDK validator; property test: baselining a report against itself yields all unchanged.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
