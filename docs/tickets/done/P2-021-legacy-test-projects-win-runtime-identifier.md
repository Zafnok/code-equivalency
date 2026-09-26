# P2-021 ShortestPaths' legacy test projects fail to load: "doesn't list 'win' as a RuntimeIdentifier"
Status: done (PR #217)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
In the 2026-09-24 census (M3-031) and again in P2-016's rerun (2026-09-25),
`pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s 6 legacy non-SDK test and example projects
(`TargetFrameworkVersion` v4.7.2) are skipped with "Your project file doesn't list 'win' as a
"RuntimeIdentifier"". M3-022 (2026-09-23, same commit, before `corpus.ps1 -Prepare`/`-Env` existed)
loaded them. Their procedures (320) are listed in `run.properties.unverified` and the run exits 4.

## Spec references
`.claude/skills/equiv-corpus-run/SKILL.md` sections 3 and 5; P2-014 (`-Prepare`, `-Env`); ADR 0029.

## Acceptance criteria (all must hold; nothing beyond them)
1. Find whether the cause is the corpus environment (`-Env`'s `TargetFrameworkRootPath`, `NoWarn`,
   the restore command the skill gives) or how MSBuildWorkspace evaluates these projects, and say
   which in Notes.
2. Fix it where it lives (`corpus.ps1` or the skill's restore step if environmental; the loader if
   not), or record why it cannot be.
3. A `census` rerun of this pair skips 0 legacy projects, or Notes say why not.

## Out of scope
Anything P2-016 fixed (multi-target flavours going Ambiguous).

## Notes
- Filed from P2-016 (its Out of scope asked for this finding to be filed separately).
- Decision (criterion 1): the loader, not the environment. Fetched the real pair
  (`TomasJohansson/adapters-shortest-paths-dotnet@e3722e971d86`) and restored the 6 legacy
  `TargetFrameworkVersion v4.7.2` test/example projects, both per-project and the whole solution,
  with `corpus.ps1 -Env`'s exact block plus `dotnet restore --force`: every one restores cleanly,
  no RID warning anywhere in the output. So `-Env`'s `TargetFrameworkRootPath`/`NoWarn` and the
  skill's restore command are not the cause. The message is NuGet's NU1004 ("Your project file
  doesn't list 'win' as a 'RuntimeIdentifier'..."), which occurs when a non-SDK PackageReference
  project's own implicit restore (triggered by MSBuildWorkspace opening it, separate from the
  solution restore the skill already ran) meets a package with a `runtimes/win/...` asset
  (`NUnit3TestAdapter`/`Microsoft.NET.Test.Sdk`, present in all 6). It only reaches
  `MsBuildSolutionLoader` (Windows: MSBuildWorkspace loads every project, ADR 0004) — Linux's
  `CompositeSolutionLoader` never sees it, since its bare loader reads the already-restored
  `project.assets.json` directly (M3-029) and never re-restores.
- Fix (criterion 2): `CompilationDiagnosticClassifier.ClassifyWorkspaceFailure` now recognises
  this message's shape (it carries no `NU1004:` prefix, matching P2-012/P2-020's NU1701/NU1702/
  NU1903/NETSDK1086) and reports it as `WorkspaceWarning`, not `WorkspaceFailure`: the packages
  are already resolved from the restore the skill runs first, so nothing downstream (the
  compilation, or the real unresolved-reference check on CS0246 etc.) changes. Same shape as the
  MSB3270/NU1701 precedent: a project whose loader-reported "failure" never touched what actually
  compiled.
- Could not run an actual `census` rerun for criterion 3 from this environment: `MsBuildSolutionLoader`
  is Windows-only (ADR 0004, needs VS 2026 Build Tools) and this session runs on Linux. Verified
  instead with `CompilationDiagnosticClassifierTests` reproducing the exact NU1004 message text
  from the real project (`Programmerare.ShortestPaths.Test.csproj`) and with a full local
  `dotnet restore` of the fetched pair showing no RID problem exists once packages are actually
  restored. The next `census` run on the Windows box is the one that closes criterion 3; flagged
  for the user to run and confirm 0 skipped legacy projects for this pair.
