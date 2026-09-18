# ADR 0009: SonarQube Cloud as a PR-blocking changegate for code smells and duplication

Status: accepted (2026-09-18)

## Context
The existing gate stack (docs/QUALITY-GATES.md, ADR 0007) enforces build/format/warnings,
100% line+branch coverage, architecture boundaries, and security (CodeQL, gitleaks,
`dotnet list package --vulnerable`, Dependabot). None of these catch code smells
(complexity, dead code, naming drift not covered by analyzers) or copy-paste duplication
across files. The user asked for a Sonar-style changegate that blocks a PR on new-code
issues, mirroring what SonarQube/SonarCloud calls a Quality Gate.

## Decision
Use SonarQube Cloud (SaaS, sonarcloud.io — formerly SonarCloud), not a self-hosted
SonarQube server. Add a scanner step to CI that runs on every PR and feeds it an
OpenCover report — SonarScanner for .NET does not read Cobertura for C#, only
OpenCover, VS coverage XML, or dotCover. `build.ps1` will emit `opencover` alongside
the existing `cobertura` output (coverlet.MTP supports multiple formats in one run);
`tools/check-coverage` keeps reading cobertura, unchanged. The scanner reports the
default "Sonar way" quality gate (new-code coverage, duplication,
maintainability/reliability/security ratings) as a PR check; the new-code coverage
condition (80%) is redundant with this repo's 100% gate and should be left as-is
rather than tuned. Start non-blocking (`continue-on-error: true`), same rollout
pattern used for Stryker in M0-004, then promote to a required check once calibrated
against a few real PRs.

## Why
- SonarQube Cloud's free tier covers private repos up to 50,000 lines of code, includes
  PR analysis and the new-code quality gate, up to 5 users, and C# support (verify
  limits at signup, as free-tier terms can change) — this repo is well under that
  ceiling at MVP stage.
- Self-hosted SonarQube Community Build is also free but does not do PR decoration or
  branch analysis on Community Build; it can only report post-merge, which does not
  give a pre-merge changegate. That defeats the reason for wanting Sonar at all.
- No new infrastructure to run or patch: it's a SaaS step in an existing GitHub Actions
  job, consistent with this repo having no other self-hosted services.
- Overlap with CodeQL is small: CodeQL is deep security dataflow analysis; Sonar's
  security rules are shallower but it adds maintainability/duplication detection that
  nothing else here covers.

## Rejected
- Self-hosted SonarQube Community Build: free, but no PR decoration / branch analysis,
  so it can't act as a pre-merge gate (only a post-merge dashboard).
- SonarQube Cloud Team plan (paid) from day one: unnecessary until the repo exceeds the
  50k LOC free-tier ceiling.
- Rolling a custom duplication/complexity check instead: reinvents a mature tool for no
  benefit; out of scope for an MVP.

## Consequences
- New external service dependency: a SonarQube Cloud account/organization and a
  `SONAR_TOKEN` GitHub Actions secret, both of which are the user's action to create
  (cannot be automated from this repo).
- `docs/adr/0002-dependencies.md` gains a `dotnet-sonarscanner (tool)` row, pinned the
  same way as `dotnet-stryker`.
- `docs/QUALITY-GATES.md` gains a new gate row once this is implemented (M0-006).
- If the repo later exceeds 50k LOC, a follow-up ADR is needed to decide whether to pay
  for the Team plan or drop the gate.
