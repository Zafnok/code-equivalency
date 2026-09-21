# M0-011 Blocking mutation gate
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-007

## Goal
QUALITY-GATES.md said the Stryker job becomes blocking at M2, but after M2 it still could
not fail: `mutation.yml` passed no `--break-at` and was `continue-on-error`, and the
2026-09-20 nightly scored `Equiv.Cli` at 69.32%. Make the gate real before M3 adds the
solver code. Pulled forward from M3-004 criterion 5, with the threshold set by the user at 90.

## Spec references
docs/QUALITY-GATES.md (Mutation row, Required checks), docs/adr/0007-testing-and-gates.md

## Acceptance criteria (all must hold; nothing beyond them)
1. `mutation.yml` passes `--break-at 90` and has no `continue-on-error`.
2. Every `src/` project scores at least 90 on a full local sweep.
3. A project with no mutants (`Equiv.Verify.Z3` today, or a PR that touches none of a
   project's files) still passes.
4. QUALITY-GATES.md lists the four `stryker (...)` checks as required; M3-004 no longer
   carries the mutation criterion. Marking them required in the ruleset is the user's action.

## Files
`.github/workflows/mutation.yml`, `tests/Equiv.Cli.Tests/CompareCommandTests.cs`,
`tests/Equiv.Cli.Tests/ProgramTests.cs`, `docs/QUALITY-GATES.md`,
`docs/tickets/M3-004-packaging.md`, `docs/ROADMAP.md` (status table refreshed after M2),
`docs/adr/0002-dependencies.md` (Z3 licence confirmed, due at M3-001).

## Tests
`Equiv.Cli.Tests` only: assert stderr text on every error path, the option definitions
(`Required`, defaults, the `--fail-on` allowlist), the null guards, and the paths the old
tests left ambiguous (only `--modern` missing; a baseline that loads).

## Size guard
No `src/` changes.

## Out of scope
Raising the threshold above 90. `--with-baseline` report storage. SonarQube promotion.

## Notes
- Survivors before (27 of 88 scored mutants): mostly `Console.Error.WriteLine` calls whose
  text no test read, the option initialisers in `CompareCommand.Create`, the two null guards,
  `||` → `&&` on the missing-file check (the only test removed both files), and
  `return true` after a baseline loads (a false return still exited 0, because the
  out-parameter exit code was `Success`).
- After: `Equiv.Cli` 97.73% locally. The two survivors are the arithmetic in
  `new List<VerificationResult>(capacity)`, which only changes the initial capacity: they
  are equivalent mutants and no test can kill them.
- Verified `--break-at` behaviour locally: `--break-at 99` on `Equiv.Cli` exits 2 ("lower
  than the configured break threshold"); `--break-at 90` on the mutant-free
  `Equiv.Verify.Z3` exits 0 ("unable to calculate a mutation score").
- `ProgramTests` now redirects the console, and xUnit v3 runs test classes in parallel, so
  it and `CompareCommandTests` share `[Collection("Console")]`.
- Decision: threshold 90 flat for every project, not "nightly score minus 2" as M3-004 had
  it. Set by the user; Core and Frontend.CSharp score about 96.5 already.
