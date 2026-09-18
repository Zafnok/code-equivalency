# M2-001 msbuild loader
Status: todo
Effort: L
Depends on: M1-005

## Goal
ISolutionLoader over MSBuildWorkspace that loads the samples' legacy (net48, old-style) and modern (net10) sides on Windows. Any WorkspaceDiagnostic of kind Failure aborts with exit 4 and the diagnostic text. Only the process-spawning call is coverage-excluded (justified with this ticket id); everything else is unit-tested against an in-memory AdhocWorkspace. Integration tests turn on in build.ps1 -Integration.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
