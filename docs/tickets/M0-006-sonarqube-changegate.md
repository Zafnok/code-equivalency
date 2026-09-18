# M0-006 SonarQube Cloud changegate
Status: todo
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
- [ ] SonarQube Cloud project created at sonarcloud.io, org linked to this repo's GitHub
      account — this is the user's action, cannot be automated; say so in the PR
- [ ] `SONAR_TOKEN` added as a GitHub Actions secret — also the user's action; say so
- [ ] New `.github/workflows/sonar.yml` (or a step added to `ci.yml`, implementer's call
      per `equiv-decide`): installs `dotnet-sonarscanner`, wraps the existing build+test
      invocation (`dotnet-sonarscanner begin` / build / `dotnet-sonarscanner end`), and
      feeds it the cobertura report `build.ps1` already produces so coverage is not
      computed twice
- [ ] `sonar-project.properties` (or scanner CLI args, implementer's call): project key,
      organization, C# analyzer inputs, exclusions for `samples/**`, `**/bin/**`,
      `**/obj/**`
- [ ] Runs on `ubuntu-latest` only (one pass is enough; avoid double-reporting from the
      Windows matrix leg)
- [ ] `continue-on-error: true` initially, same pattern as `mutation.yml` in M0-004;
      a `Decision:` note in this ticket's Notes records when/how it gets promoted to
      blocking
- [ ] `docs/QUALITY-GATES.md`: new gate table row ("Code smells / duplication" —
      SonarQube Cloud — Sonar Quality Gate on new code — non-blocking until promoted),
      and a note under "Required checks" once promoted
- [ ] Action pinned by commit SHA, per this repo's existing convention (CLAUDE.md,
      M0-004)

## Out of scope
Self-hosted SonarQube (rejected in ADR 0009). Migrating or removing any existing
CodeQL/Meziantou.Analyzer findings — those stay as-is; Sonar is additive. Paying for the
Team plan — only relevant if the repo exceeds the 50k LOC free-tier ceiling, which would
need its own ADR.

## Notes
Left empty by the author; the implementer records surprises.
