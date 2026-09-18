# M0-004 GitHub Actions
Status: todo
Effort: M
Depends on: M0-003

## Goal
Every PR runs the full gate set on Windows and Ubuntu; security scans run; mutation
score is reported.

## Deliverables
- [ ] `.github/workflows/ci.yml`: matrix `windows-latest` + `ubuntu-latest`; the job calls
      `build.ps1` (single source of truth) with `-Integration` on Windows only. Upload
      cobertura and coverage summary as artefacts. Cancel in-progress runs on the same ref.
- [ ] `.github/workflows/codeql.yml` (C#).
- [ ] `.github/workflows/mutation.yml`: Stryker 5 with `--test-runner mtp`,
      `continue-on-error: true`, HTML report as artefact; nightly schedule plus on PR.
- [ ] `.github/dependabot.yml` (nuget + github-actions, weekly). gitleaks step in `ci.yml`.
- [ ] `dotnet list package --vulnerable --include-transitive` step that fails on any hit.
- [ ] Required-checks list added to `docs/QUALITY-GATES.md`; applying branch protection on
      GitHub is the user's action, say so in the PR.
- [ ] Every action pinned by commit SHA.

## Out of scope
Release/publish workflow (M3-004).

## Notes
