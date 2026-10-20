# P2-267 Precision bug: a runtime-changed call inside a lambda the body only subscribes to an event makes the body an exposure
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A body that attaches a lambda to an event (`control.Click += (s, e) => Process.Start(...)`) never runs the lambda
itself; the lambda is its own procedure, run when the event fires. P2-247
(`docs/runs/2026-10-20-precision-execute-verdict-2.md`, appendix C) found two Git Extensions exposures whose only
runtime-changed call (`Path.GetDirectoryName`, `Process.Start`) sits in a `Selecting` or `Click` handler that the
method subscribes and never invokes (`U03`, `U11` of appendix C): both false positives. When done, a runtime-changed call
that is only inside a lambda the body stores or subscribes and does not invoke does not make the body an exposure.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, the fingerprint's runtime-rule component (ADR 0060), ticket
P2-209 (a runtime-changed call inside a lambda in an `abstraction` Unknown), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test: `button.Click += (s, e) => Process.Start(p);` in an unedited body is congruent, not EQ007; a body that
   invokes the lambda it builds is still an exposure.
2. `gitextensions-8522` in `full`: both results are given with their result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247.
