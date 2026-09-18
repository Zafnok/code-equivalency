# M1-001 samples v1
Status: todo
Effort: M
Depends on: M0-004

## Goal
Five paired sample solutions under samples/, each with a README stating expected verdicts per procedure. Legacy side: old-style csproj, net48, no NuGet. Modern side: SDK-style net10.0. Same namespaces and member names on both sides except where the sample is about renames. Samples: identical, renamed-locals, added-branch, removed-null-check, loop-bound-change. Each side builds with its own toolchain (verified manually in this ticket, not in CI).

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
