# M3-027 Z3 5.1.0 from the official GitHub release, on every platform
Status: done (PR #163)
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
`docs/tickets/done/M3-001-z3-product-encoder.md`, `docs/tickets/M3-004-packaging.md`.

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
- Note: verified the release asset directly rather than trusting a copied hash: downloaded
  `https://github.com/Z3Prover/z3/releases/download/z3-5.1.0/Microsoft.Z3.5.1.0.nupkg` (65 MB),
  computed its SHA-256 (`d808e6bd31d96895eca446eceb37e90dd24480b0d9b5f513113f3e2fd312abf2`, checked
  into `tools/z3-feed/Microsoft.Z3.5.1.0.nupkg.sha256`), and inspected the nupkg contents: nuspec
  `<license type="expression">MIT</license>`, `lib/netstandard2.0/Microsoft.Z3.dll`, and
  `runtimes/{linux-x64,linux-arm64,osx-x64,osx-arm64,win-x64,win-arm64}/native/` all present.
- Note: `fetch.ps1`'s three paths (fresh download, idempotent skip when the cached file already
  matches, and hash-mismatch delete-and-fail) were each exercised directly, including corrupting
  the pinned hash and confirming the mismatched download is deleted and the script exits non-zero.
- Decision: the "Provide libz3 (Linux)" PyPI-wheel step is removed from `mutation.yml` and
  `sonar.yml` too, not only `ci.yml` (criterion 5 names only `ci.yml`, but the identical step was
  copy-pasted into all three). Left in place it would still pin `z3-solver==4.12.2.0`'s
  `libz3.so` onto `LD_LIBRARY_PATH`, shadowing the 5.1.0 native the package now bundles, for two
  required checks. Alternatives: leave them (wrong libz3 version loaded on those legs). Rule: 4.
- Decision: `codeql.yml` gets the same `tools/z3-feed/fetch.ps1` step as the other three
  workflows (criterion 4 names it explicitly), even though `build-mode: none` means this
  workflow currently runs no `dotnet` command and so does not strictly need the feed yet. Keeps
  the four workflows uniform and avoids a silent gap if CodeQL's build mode ever changes.
  Rule: 4.
- Note: on 5.1.0 the self-comparison properties
  (`SoundnessPropertyTests.AProcedureIsEquivalentToItself`,
  `RenamingTheParametersKeepsAProcedureEquivalent`, `LadderPropertyTests.ALoopingProcedureIsEquivalentToItself`)
  each hit about one `Unknown` per 200 draws. It was a genuine timeout: `ReasonUnknown` was
  `timeout`, returned at the deadline (5,161 ms at a 5 s timeout, 15,118 ms at 15 s, still unknown
  at 120 s). On 4.12.2 the same fixture was `Equivalent` in 104-147 ms. Cause, from dumping the goal
  after preprocessing: 4.12.2's pipeline reduces the whole goal to `false`, while 5.1.0's first
  `solve-eqs` inverts the definition `old.t25 = old.t24 + in.b` and eliminates the shared input
  `in.b` instead of `old.t25`. After that neither side's definitions can be eliminated and the two
  sides stay different terms, so `smt` has to bit-blast a 32-bit multiply. `solve-eqs` parameters
  do not stop it: `theory_solver=false`, `ite_solver=false`, `context_solve` and even
  `solve_eqs_max_occs=0` all produced a byte-identical goal. Parameters are validated, since an unknown
  name throws, so this is not a typo. Adding `elim-term-ite`, or using a plain `QF_AUFBV` solver,
  did not help either.
- Decision: `Z3Backend.Query` substitutes the encoding's definitions into the query before adding it
  (`Z3Backend.Inline`). A lone Bool constant is `true`, and `(= c t)` with a constant `c` is `t`
  with the earlier definitions substituted, in assertion order. The definitions stay asserted, so
  the query is equivalent and models are unchanged. Identical computations on both sides are now one
  hash-consed term before Z3 sees them, so this does not depend on which variable `solve-eqs`
  chooses to eliminate. The failing fixture is `Equivalent` in 79 ms, the strict
  `IsType<Equivalent>` assertions stay as they were, and `Equiv.Verify.Z3.Tests` went from
  25-42 s to 14-18 s a run. Tests: `Z3BackendTests.InliningSubstitutesDefinitionsInOrderAndSkipsEverythingElse`
  and `ASelfComparisonWhoseInputFeedsAnAdditionIsEquivalent`, which is `Unknown` when `Inline` is
  bypassed. Alternatives: relax the properties to `IsNotType<Divergent>` (tried first and reverted
  in review, because it lets a backend that always answers `Unknown` pass); tune the solver (no
  parameter changed the outcome). Rule: 4.
- Note: the Unknown-rate baseline predates this ticket. M3-031 scored the Git Extensions census on
  4.12.2, and 5.1.0 plus `Inline` changes which queries time out. Re-run the census before comparing
  any later Unknown rate against M3-031's; a pointer is in M3-031's Notes.
- Note: the samples-backed verdict tests (`Equiv.Tests.Integration`'s `ComparePipelineTests`,
  `LoopLadderSampleTests`) were run directly against 5.1.0 and are unchanged; the Goal's
  "verdicts on the samples stay the same" holds without any snapshot updates. Two unrelated
  `Equiv.Tests.Integration` tests (`EndpointDiscoverySampleTests`) fail on this box for environment
  reasons that predate this ticket: the legacy `webapi-basic` sample needs `build.ps1 -Integration`'s
  MSBuild restore step, which this box's local run skipped.
- Note (review): with `.z3-feed/` absent, a restore that needs only nuget.org packages still succeeds
  (no NU1301), because package source mapping never queries the local feed for them. This was checked
  with an empty `--packages` folder, on both a repo project and a copy of `corpus.ps1`'s
  reference-assembly restore under `.corpus/`. So the corpus script and Dependabot's non-Z3 updates
  do not need the fetch.
- Note (review): every workflow caches `.z3-feed/` with `actions/cache`, keyed on the pinned
  `.sha256`, before the fetch runs. `fetch.ps1` is a no-op on a hash match, so most runs skip the
  65 MB download. `fetch.ps1` turns off the progress bar, which slows Windows PowerShell 5.1
  downloads a lot. README, CONTRIBUTING and the `equiv-corpus-run` skill say to run `fetch.ps1`
  before any direct `dotnet restore`/`build`.
- Note: `tools/licence-check --fix` regenerated `THIRD-PARTY-NOTICES.md` (only the `Microsoft.Z3`
  row's version changed); the licence gate passes for all 77 restored packages.
- Note: criterion 6's post-merge Dependabot verification cannot be done before this PR merges.
  After merge, trigger a Dependabot NuGet check (repo Insights > Dependency graph > Dependabot >
  "Check for updates") and link the resulting run/PR here, or record what failed if the feed
  being absent from a Dependabot-triggered run breaks it.
