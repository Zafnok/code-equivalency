# M0-006 SonarQube Cloud changegate
Status: done (PR #17)
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
      (done by the user after the initial PR; confirmed live in CI logs)
- [x] `SONAR_TOKEN` added as a GitHub Actions secret — also the user's action; say so
      (done by the user after the initial PR; confirmed live in CI logs)
- [x] New `.github/workflows/sonar.yml` (or a step added to `ci.yml`, implementer's call
      per `equiv-decide`): installs `dotnet-sonarscanner`, wraps the existing build+test
      invocation (`dotnet-sonarscanner begin` / build / `dotnet-sonarscanner end`), and
      feeds it the cobertura report `build.ps1` already produces so coverage is not
      computed twice
- [x] `sonar-project.properties` (or scanner CLI args, implementer's call): project key,
      organization, C# analyzer inputs, exclusions for `samples/**`, `**/bin/**`,
      `**/obj/**` (**scanner CLI args** — see Decision below; a properties file turned out
      not to be an option)
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
- Decision (superseded, see below): static Sonar config location -> originally
  `sonar-project.properties` for exclusions and the opencover report path, with project
  key/org/token/host as scanner CLI args. Reverted: SonarScanner for .NET refuses to run at all
  if a `sonar-project.properties` file exists anywhere in the repo ("sonar-project.properties
  files are not understood by the SonarScanner for .NET" — that file format is
  SonarScanner-for-Java-only). Removed the file; everything (exclusions, opencover path, project
  key/org/token/host) is now a `/d:`/`/k:`/`/o:` arg on `dotnet sonarscanner begin`.
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
- Update: user created the SonarCloud project and added `SONAR_TOKEN` after the initial PR. The
  live run confirmed `sonar begin` correctly detects incremental PR analysis against `main`
  (new-code-only quality gate, as designed in ADR 0009) — but the wrapped `./build.ps1` call
  itself failed to compile, unrelated to any Sonar quality gate verdict. See next Decision.
- Decision: PR was failing because `dotnet sonarscanner begin` injects SonarAnalyzer.CSharp into
  the compile, and this repo's `-warnaserror` promoted its diagnostics on pre-existing code
  (`AssemblyMarker.cs`, `tools/check-coverage/*.cs` — none of it touched by this PR) into hard
  compiler errors, failing the build before Sonar's own quality gate (which already correctly
  scopes to new code) ever ran. The same analyzer also made `dotnet format --verify-no-changes`
  fail (it wanted to apply the analyzer's suggested fixes). Fixed by adding a `-SonarBuild` switch
  to `build.ps1` — used only by `sonar.yml` — that drops `-warnaserror` and skips the format step
  entirely (format is already fully enforced, unconditionally, by `ci.yml`'s `gates` job).
  Alternatives considered: (a) fix every flagged pre-existing file now — out of scope for this
  ticket and reopens old, already-shipped tickets; (b) suppress specific Sonar rules via
  `.editorconfig` — hides real findings from the Sonar dashboard too, which the user wants kept
  visible; (c) don't reuse `build.ps1` for the Sonar job, hand-roll a separate build+test —
  duplicates ~20 lines the ticket already asked to reuse. Rule: 4 (smaller change) — a scoped,
  documented switch that only the non-blocking Sonar job passes; the strict gates in `ci.yml` are
  untouched and still enforce `-warnaserror` and `dotnet format --verify-no-changes` everywhere.
- Verified locally: `./build.ps1 -SonarBuild` builds, tests, and reports coverage cleanly (same
  as the default run, minus the format step and minus `-warnaserror`); `./build.ps1` (no switch)
  is unaffected and still runs format with `-warnaserror`.
