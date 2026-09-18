# M1-001 samples v1
Status: todo
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
- [ ] `samples/Directory.Build.props` (empty `<Project />`): stops the root
      `Directory.Build.props` (net10.0, TreatWarningsAsErrors, CPM, MinVer/Meziantou refs)
      from leaking into sample projects: each side must build with only its own toolchain.
- [ ] `samples/identical/{legacy,modern}`: `Calculator` with `Add`/`Max`, byte-for-byte
      same logic both sides. `README.md` states both procedures are Equivalent.
- [ ] `samples/renamed-locals/{legacy,modern}`: same `Calculator.Add`/`Max` logic, modern
      side renames local variables only (no member/namespace rename). `README.md` states
      both procedures are Equivalent (local names are erased by SSA).
- [ ] `samples/added-branch/{legacy,modern}`: `Doubler.Double(int)`; modern adds an early
      special case for `x == 0`. `README.md` states Divergent, counterexample `x = 0`.
- [ ] `samples/removed-null-check/{legacy,modern}`: `Greeter.Greet(string)`; legacy throws
      `ArgumentNullException` on null input, modern removes the check and lets a
      `NullReferenceException` surface instead. `README.md` states Divergent (different
      exception type on null input).
- [ ] `samples/loop-bound-change/{legacy,modern}`: `Summation.SumUpTo(int)`; modern's loop
      condition is off-by-one (`<=` vs `<`). `README.md` states Divergent, counterexample
      `n = 1`, provable at bounded unrolling `k = 2` (rung 1).
- [ ] Each side is a real solution: legacy is an old-style csproj (net48, no NuGet) plus a
      classic `.sln`; modern is an SDK-style csproj (net10.0) plus a `.slnx`, matching the
      root's "XML solution format, no `.sln`" rule from CLAUDE.md.
- [ ] Manually verify (not wired into `build.ps1`, per the goal): legacy side builds with
      `MSBuild.exe` from VS 2026 Build Tools; modern side builds with `dotnet build`.
      Record the exact commands and results in Notes.
- [ ] tests: unit — `tests/Equiv.Tests.Integration/SamplesFixtureTests.cs` asserts, for
      each of the 5 samples, that `legacy/*.sln` + `legacy/*.csproj` and
      `modern/*.slnx` + `modern/*.csproj` exist and that `README.md` exists and mentions
      every expected verdict named above. No MSBuild/Roslyn invocation (that starts in
      M2-001); this only pins the fixture shape so a later ticket cannot silently change it.

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
