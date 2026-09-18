# M1-001 samples v1
Status: done (PR #13)
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-004

## Goal
Five paired sample solutions under samples/, each with a README stating expected verdicts per procedure. Legacy side: old-style csproj, net48, no NuGet. Modern side: SDK-style net10.0. Same namespaces and member names on both sides except where the sample is about renames. Samples: identical, renamed-locals, added-branch, removed-null-check, loop-bound-change. Each side builds with its own toolchain (verified manually in this ticket, not in CI).

## Spec references
ARCHITECTURE.md "Components" (Equiv.Frontend.CSharp loader expectations) and "Data flow";
VERIFICATION-MODEL.md section 1 (what Equivalent/Divergent mean), section 3
(migration-specific normalisations, out of scope here since namespaces/names match) and
section 5.1 (loop ladder rung 1, referenced by loop-bound-change's expected verdict).

## Deliverables
- [x] `samples/Directory.Build.props` (empty `<Project />`): stops the root
      `Directory.Build.props` (net10.0, TreatWarningsAsErrors, CPM, MinVer/Meziantou refs)
      from leaking into sample projects: each side must build with only its own toolchain.
- [x] `samples/identical/{legacy,modern}`: `Calculator` with `Add`/`Max`, byte-for-byte
      same logic both sides. `README.md` states both procedures are Equivalent.
- [x] `samples/renamed-locals/{legacy,modern}`: same `Calculator.Add`/`Max` logic, modern
      side renames local variables only (no member/namespace rename). `README.md` states
      both procedures are Equivalent (local names are erased by SSA).
- [x] `samples/added-branch/{legacy,modern}`: `Doubler.Double(int)`; modern adds an early
      special case for `x == 0`. `README.md` states Divergent, counterexample `x = 0`.
- [x] `samples/removed-null-check/{legacy,modern}`: `Greeter.Greet(string)`; legacy throws
      `ArgumentNullException` on null input, modern removes the check and lets a
      `NullReferenceException` surface instead. `README.md` states Divergent (different
      exception type on null input).
- [x] `samples/loop-bound-change/{legacy,modern}`: `Summation.SumUpTo(int)`; modern's loop
      condition is off-by-one (`<=` vs `<`). `README.md` states Divergent, counterexample
      `n = 1`, provable at bounded unrolling `k = 2` (rung 1).
- [x] Each side is a real solution: legacy is an old-style csproj (net48, no NuGet) plus a
      classic `.sln`; modern is an SDK-style csproj (net10.0) plus a `.slnx`, matching the
      root's "XML solution format, no `.sln`" rule from CLAUDE.md.
- [x] Manually verify (not wired into `build.ps1`, per the goal): legacy side builds with
      `MSBuild.exe` from VS 2026 Build Tools; modern side builds with `dotnet build`.
      Record the exact commands and results in Notes.
- [x] tests: unit — `tests/Equiv.Tests.Integration/SamplesFixtureTests.cs` asserts, for
      each of the 5 samples, that `legacy/*.sln` + `legacy/*.csproj` and
      `modern/*.slnx` + `modern/*.csproj` exist and that `README.md` exists and mentions
      every expected verdict named above. No MSBuild/Roslyn invocation (that starts in
      M2-001); this only pins the fixture shape so a later ticket cannot silently change it.

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes

- Manual build verification (Debug, this box):
  - Modern: `dotnet build` inside each `samples/<name>/modern/` directory. All 5 succeed,
    0 warnings, 0 errors.
  - Legacy: `& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    samples\<name>\legacy\*.sln /p:Configuration=Debug /verbosity:minimal` for each of the
    5 samples. All succeed, 0 warnings, 0 errors. In Git Bash, `/p:` and `/verbosity:`
    get mangled by MSYS path conversion into `p:`/`v:m` (MSB1008); set
    `MSYS_NO_PATHCONV=1` (or run from PowerShell) to avoid it.
- Added `samples/Directory.Build.props` as an empty `<Project />`. MSBuild resolves
  `Directory.Build.props` by walking up from the project directory and importing only the
  *nearest* one, so this fully replaces (not merges with) the root's — the root's
  `TargetFramework=net10.0`, `TreatWarningsAsErrors`, `AnalysisLevel=latest-all`,
  `ManagePackageVersionsCentrally`, and the global `MinVer`/`Meziantou.Analyzer`
  `PackageReference`s never reach sample projects. This is necessary for the legacy
  net48 old-style csproj (which auto-imports `Directory.Build.props` via
  `Microsoft.Common.props` the same as SDK-style projects since VS15) and keeps both
  sides honestly on "their own toolchain" as the ticket goal requires.
- `removed-null-check`: a plain `"Hello, " + name` with `name == null` would NOT throw in
  C# (string concatenation treats a null operand as `""`), so there'd be nothing to
  diverge on. Used `name.ToUpper()` instead so the missing check surfaces as a real
  `NullReferenceException`, matching VERIFICATION-MODEL.md's null-dereference model
  (section 2: reference values carry an "is null" shadow that a dereference branches on).
- Fixture test (`SamplesFixtureTests.cs`) lives in `Equiv.Tests.Integration` since that's
  the project `tests/README.md` designates for everything under `samples/`; it does no
  MSBuild/Roslyn work so it runs happily even without `-Integration` (verified via a
  standalone `dotnet test` on the project) — `-Integration` only changes whether
  `build.ps1` includes this *project* in its loop, not what the tests inside it may do.
- Two Meziantou/NetAnalyzer hits while writing the fixture test, both real and fixed:
  `MA0002` (use `StringComparer.Ordinal` for the `Dictionary<string,_>`) and `CA1307`
  (`Assert.Contains` needs an explicit `StringComparison.Ordinal`). Left as evidence the
  100%-warnings-as-errors gate is doing its job even on test code.
