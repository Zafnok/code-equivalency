# M0-006 SonarQube Cloud changegate
Status: in-progress
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-005, ADR 0009 (must be accepted before this ticket starts)

## Goal
Add a SonarQube Cloud analysis step to CI that reports code smells, duplication, and
maintainability/reliability ratings on every PR, and enforces the Sonar Quality Gate as
a PR check — filling the gap CodeQL, the analyzers, and the coverage gate don't cover
(duplication across files, complexity, dead code). Starts non-blocking; promoted to a
required check once calibrated, mirroring how Stryker was rolled out in M0-004.

## Spec references
docs/QUALITY-GATES.md (gate table), docs/adr/0009-sonarqube-cloud-changegate.md,
docs/adr/0007-testing-and-gates.md

## Deliverables
- [x] SonarQube Cloud project created at sonarcloud.io, org linked to this repo's GitHub
      account — this is the user's action, cannot be automated; say so in the PR
      (**not done — user action required**, see Notes)
- [x] `SONAR_TOKEN` added as a GitHub Actions secret — also the user's action; say so
      (**not done — user action required**, see Notes)
- [x] New `.github/workflows/sonar.yml` (or a step added to `ci.yml`, implementer's call
      per `equiv-decide`): installs `dotnet-sonarscanner`, wraps the existing build+test
      invocation (`dotnet-sonarscanner begin` / build / `dotnet-sonarscanner end`), and
      feeds it the cobertura report `build.ps1` already produces so coverage is not
      computed twice
- [x] `sonar-project.properties` (or scanner CLI args, implementer's call): project key,
      organization, C# analyzer inputs, exclusions for `samples/**`, `**/bin/**`,
      `**/obj/**`
- [x] Runs on `ubuntu-latest` only (one pass is enough; avoid double-reporting from the
      Windows matrix leg)
- [x] `continue-on-error: true` initially, same pattern as `mutation.yml` in M0-004;
      a `Decision:` note in this ticket's Notes records when/how it gets promoted to
      blocking
- [x] `docs/QUALITY-GATES.md`: new gate table row ("Code smells / duplication" —
      SonarQube Cloud — Sonar Quality Gate on new code — non-blocking until promoted),
      and a note under "Required checks" once promoted (deferred until promotion, per
      Decision below)
- [x] Action pinned by commit SHA, per this repo's existing convention (CLAUDE.md,
      M0-004)

## Out of scope
Self-hosted SonarQube (rejected in ADR 0009). Migrating or removing any existing
CodeQL/Meziantou.Analyzer findings — those stay as-is; Sonar is additive. Paying for the
Team plan — only relevant if the repo exceeds the 50k LOC free-tier ceiling, which would
need its own ADR.

## Notes
- Decision: workflow file vs. `ci.yml` step -> new `.github/workflows/sonar.yml`. Alternatives:
  add a step to `ci.yml`'s `gates` job. Rule: 1 (mirror the consumer) — the scanner needs a JRE
  (`actions/setup-java`) that the `gates` matrix doesn't otherwise need, and it must run once on
  `ubuntu-latest` only, not per matrix leg; a separate file mirrors how `mutation.yml` was split
  out in M0-004 for the same reason (different tool lifecycle, non-blocking rollout).
- Decision: static Sonar config location -> `sonar-project.properties` for exclusions and the
  opencover report path; project key/org/token/host stay as scanner CLI args in the workflow.
  Alternatives: everything as `/d:` CLI args, everything in the properties file. Rule: 4 (smaller
  change) — secrets and PR-specific values don't belong in a committed file; static repo-shape
  config (exclusions, report path) doesn't belong duplicated across workflow runs.
- Decision: coverage report format -> added `--coverlet-output-format opencover` alongside the
  existing `cobertura` in `build.ps1`'s `dotnet test` invocation (coverlet.MTP accepts the flag
  repeated for multiple formats). Alternatives: a separate coverage run just for Sonar. Rule: 4 —
  one test run already exists; `tools/check-coverage` globs `*.cobertura.*.xml` specifically, so
  the new `*.opencover.*.xml` files alongside it don't affect the existing coverage gate.
- Decision: project key / organization -> placeholders `Zafnok_code-equivalency` / `zafnok` in
  `.github/workflows/sonar.yml`. These are guesses at SonarCloud's default naming; the user must
  create the actual project (deliverable 1, not automatable) and correct these two `/k:`/`/o:`
  values if SonarCloud assigned different ones. Rule: 4 — a placeholder consistent with SonarCloud's
  documented default key format is a smaller change than leaving the workflow non-functional.
- Verified locally: `./build.ps1` produces `TestResults/<Project>.coverage.opencover.<ts>.xml`
  alongside the existing cobertura files, matching the `sonar-project.properties` glob
  (`TestResults/**/*.opencover.*.xml`); all gates still green.
- Promotion to a required/blocking check is a follow-up decision once a few real PRs have run
  through it (per ADR 0009); not done in this ticket. No `docs/QUALITY-GATES.md` "Required
  checks" note is added until then.
- Could not run the actual SonarQube Cloud scan end-to-end (no `SONAR_TOKEN` secret exists yet;
  deliverable 1/2 are user actions). The workflow is untested against a live SonarCloud project —
  flag this in PR review once the user adds the token and project.
