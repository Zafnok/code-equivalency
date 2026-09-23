# M3-027 Z3 5.1.0 from the official GitHub release, on every platform
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-002

## Goal
`Microsoft.Z3` moves from 4.12.2 (nuget.org, December 2023) to 5.1.0, restored from the official
Z3Prover/z3 GitHub release nupkg through a hash-pinned local feed (ADR 0030). The PyPI wheel step
in CI goes away, because the new package carries `libz3` for linux-x64 itself. Verdicts on the
samples stay the same, or each change is explained.

## Spec references
ADR 0030; ADR 0002 (Microsoft.Z3 row); ADR 0005; M3-001 Notes (Linux native gap).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/z3-feed/fetch.ps1` downloads
   `https://github.com/Z3Prover/z3/releases/download/z3-5.1.0/Microsoft.Z3.5.1.0.nupkg` into
   `.z3-feed/`, checks it against `tools/z3-feed/Microsoft.Z3.5.1.0.nupkg.sha256`, deletes it and
   exits non-zero on mismatch, and does nothing if a matching file is already there.
   `.z3-feed/` is gitignored.
2. `NuGet.config` at the repo root: sources `nuget.org` and `z3-feed` (`./.z3-feed`), package
   source mapping `Microsoft.Z3` → `z3-feed`, `*` → `nuget.org`.
3. `Directory.Packages.props` pins `Microsoft.Z3` 5.1.0.
4. `build.ps1` runs the fetch script before restore. Every workflow that restores (`ci.yml`,
   `mutation.yml`, `sonar.yml`, `codeql.yml`) runs it before its first `dotnet` command.
5. The "Provide libz3 (Linux)" step and its PyPI hash are deleted from `ci.yml`; the Ubuntu
   gates leg passes with the `libz3.so` from the package.
6. `.github/dependabot.yml` ignores `Microsoft.Z3`, and after merge, a Dependabot NuGet run
   succeeds for the other packages (link the run or PR in Notes). If it fails because the feed is
   missing, stop and write what you saw in Notes.
7. All existing tests pass unchanged, or each changed snapshot / verdict is listed in Notes with
   the reason (a Z3 behaviour change the test was pinned to). A verdict that flips between
   Equivalent and Divergent is a stop condition, not a snapshot update.
8. ADR 0002's `Microsoft.Z3` row gives the version, the source (GitHub release, ADR 0030), the
   six platforms, and the glibc 2.38 floor. README's system requirements state the glibc floor.
9. M3-001 and M3-004 Notes that describe the Linux wheel workaround get a one-line pointer to this
   ticket; M3-004's Linux libz3 question is marked answered.

## Files
`tools/z3-feed/fetch.ps1`, `tools/z3-feed/Microsoft.Z3.5.1.0.nupkg.sha256`, `NuGet.config`,
`.gitignore`, `Directory.Packages.props`, `build.ps1`, `.github/workflows/{ci,mutation,sonar,codeql}.yml`,
`.github/dependabot.yml`, `docs/adr/0002-dependencies.md`, `README.md`,
`docs/tickets/M3-001-z3-product-encoder.md`, `docs/tickets/M3-004-packaging.md`.

## Tests
No new tests; the existing `Equiv.Verify.Z3.Tests`, integration and property suites on both CI
legs are the check. Z3 API breaks surface as build errors and are fixed in `src/Equiv.Verify.Z3` only.

## Size guard
More than 5 files changed under `src/`, or any change outside `src/Equiv.Verify.Z3`, means the
upgrade changed behaviour: stop and write it up.

## Out of scope
New Z3 features (FiniteSets, new tactics), solver parameter tuning, arm64 CI legs, the
container (M3-004), Linux loading (M3-028).

## Notes
