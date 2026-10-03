# P2-096 A body whose only diagnostics are warnings promoted to errors is not `unbound`
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-085, P2-058

## Goal
On `powershell-19687`, 82 pairs are Unknown(`unbound`) on both sides. The projects set
`TreatWarningsAsErrors`. The commit pins a .NET 8 preview, and against the released net8.0
reference pack it reports SYSLIB0051, CS0672 and SYSLIB0050, the obsoletion of formatter-based
serialization. Those are warnings. The bodies bind: every symbol resolves and every operation has
a type. A warning the project chose to promote says nothing about whether `equiv` can read the
body. Treat a body as `unbound` only when it has a diagnostic whose default severity is error.

## Spec references
ADR 0029 and its 2026-10-01 clarification (what `unbound` means); P2-085;
`docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Apply `equiv-adr`'s bar test first, because this narrows ADR 0029's clarification. Record the
   outcome in Notes.
2. First confirm the cause on the pair: Notes list the diagnostic ids behind the 82 results. If
   any of them has default severity error, say so and keep that body `unbound`.
3. A fixture project with `TreatWarningsAsErrors` and a body that calls an obsolete member lowers
   that body. A body with a real binding error (CS0103) in the same project stays `unbound`.
4. The same rule decides whether a project is skipped at load.
5. A rerun of `powershell-19687` reports its `unbound` count in Notes. It was 82.

## Tests
- `CSharpFrontendTests.AWarningPromotedToAnErrorDoesNotUnbindABody`
- `CSharpFrontendTests.ARealBindingErrorStillUnbindsABody`

## Out of scope
Fixing the corpus checkout. Analyzer diagnostics, which the loader does not run.

## Notes
