# M0-004 GitHub Actions
Status: done (PR #4)
Effort: M
Depends on: M0-003

## Goal
Every PR runs the full gate set on Windows and Ubuntu; security scans run; mutation
score is reported.

## Deliverables
- [x] `.github/workflows/ci.yml`: matrix `windows-latest` + `ubuntu-latest`; the job calls
      `build.ps1` (single source of truth) with `-Integration` on Windows only. Upload
      cobertura and coverage summary as artefacts. Cancel in-progress runs on the same ref.
- [x] `.github/workflows/codeql.yml` (C#).
- [x] `.github/workflows/mutation.yml`: Stryker 5 with `--test-runner mtp`,
      `continue-on-error: true`, HTML report as artefact; nightly schedule plus on PR.
- [x] `.github/dependabot.yml` (nuget + github-actions, weekly). gitleaks step in `ci.yml`.
- [x] `dotnet list package --vulnerable --include-transitive` step that fails on any hit.
- [x] Required-checks list added to `docs/QUALITY-GATES.md`; applying branch protection on
      GitHub is the user's action, say so in the PR.
- [x] Every action pinned by commit SHA.

## Out of scope
Release/publish workflow (M3-004).

## Notes
- `build.ps1` had no `-Integration` switch and ran `dotnet test` solution-wide, which would
  always include `Equiv.Tests.Integration`. Rewrote the `test` step to loop over discovered
  test `.csproj` files and skip `Equiv.Tests.Integration` unless `-Integration` is passed, so
  `ci.yml` can run it on `windows-latest` only, per QUALITY-GATES.md's CI matrix.
- `check-coverage`'s report only went to stdout, so there was nothing to upload as a "coverage
  summary" artefact. `build.ps1` now tees it to `TestResults/coverage-summary.txt`.
- No `.config/dotnet-tools.json` existed yet even though `dotnet-stryker` 5.0.0 is already
  registered in `docs/adr/0002-dependencies.md` (M0-002). Added the manifest; no new ADR
  entry needed since the dependency was already approved.
- Stryker.NET 5 has no solution-wide mode; it mutates one src/test project pair per
  invocation (`-p`/`--project` resolved via the test project's references). `mutation.yml`
  matrixes over the four src/test pairs instead.
- `dotnet stryker` (Buildalyzer) picked up VS 2026 Build Tools' standalone `MSBuild.exe`
  instead of the .NET SDK and failed to resolve `Microsoft.NET.Sdk` for every project
  (`MSB4276`, directory `...BuildTools\MSBuild\Sdks\Microsoft.NET.Sdk\Sdk` doesn't exist —
  same underlying VS2026 layout issue ADR 0002 mentions for Roslyn). Passing
  `--target-framework net10.0` makes Stryker skip the failing target-framework probe and
  build via `dotnet build` directly; added it to every `dotnet stryker` invocation in
  `mutation.yml`. Verified locally against `Equiv.Core` (0 mutants currently — the MVP has
  no mutable logic yet, so mutation score isn't meaningful until later milestones; that's
  expected and is why the row is `continue-on-error` and non-blocking until M2).
- `dotnet list package --vulnerable` always exits 0; detection is via grepping stdout for
  "has the following vulnerable packages" (verified locally — the clean-repo wording is
  "has no vulnerable packages given the current sources").
- Action SHAs pinned to the newest stable tag as of 2026-09-18 (resolved via the GitHub API,
  annotated tags dereferenced to their commit): `actions/checkout` v4.2.2, `actions/setup-dotnet`
  v4.3.1, `actions/upload-artifact` v4.6.2, `github/codeql-action` v3.28.11, `gitleaks/gitleaks-action`
  v2.3.9. Dependabot (`github-actions` ecosystem) will keep these current.
