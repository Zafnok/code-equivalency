# M3-004 packaging
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-003

## Goal
dotnet publish single-file for win-x64 and linux-x64 (the linux build succeeds and the loader reports unsupported until the bare loader exists); multi-stage Dockerfile; action.yml wrapping the container with upload-sarif to GitHub Code Scanning; release.yml on tag via MinVer. Stryker job becomes blocking at its current score minus 2 points.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, plus the kinds named in the goal

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
