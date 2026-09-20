# CLAUDE.md — rules for working in this repo

You are implementing a plan that has already been decided. Do not re-architect. If a
decision looks wrong, write a short ADR proposing the change (see `.claude/skills/equiv-adr`)
and stop; do not silently deviate.

## How work is picked up

- Work is defined in `docs/tickets/`. One ticket = one PR. Do the ticket, nothing more.
- Follow `.claude/skills/equiv-task-loop/SKILL.md` for every ticket. It is the definition of done.
- Milestones and ordering are in `docs/ROADMAP.md`. Do not start a ticket whose
  dependencies are not merged.
- One exception: SonarQube debt is tracked as GitHub issues labelled `sonar`, not as ticket
  files. The issue body is the ticket; fix one with `.claude/skills/equiv-sonar-fix` (ADR 0016).

## Non-negotiables (enforced by CI; see docs/QUALITY-GATES.md)

- 100% line and branch coverage on every `src/` project. No exclusions without an
  `[ExcludeFromCodeCoverage(Justification = "...")]` whose justification names a ticket.
- Warnings are errors. `AnalysisLevel=latest-all`, nullable enabled, `dotnet format` clean.
- Every public behaviour has: a unit test, and where the ticket says so, a property test
  (CsCheck), a snapshot test (Verify), or an integration test against `samples/`.
- Architecture tests (ArchUnitNET) enforce the dependency rules in `docs/ARCHITECTURE.md`.
- No new NuGet package without a line in `docs/adr/0002-dependencies.md`.
- Output is SARIF 2.1.0. Never invent a parallel result schema.

## Style

- .NET 10 / C# 14. `file`-scoped namespaces, records for data, `sealed` by default,
  `internal` by default, `InternalsVisibleTo` for the matching test project only (plus `Equiv.Tests.Integration`
  where a ticket puts integration tests for an internal contract there, e.g. M2-001).
- No reflection, no dynamic, no `#pragma warning disable` (use `.editorconfig` severity
  with a comment if a rule is genuinely wrong for this repo, and mention it in the PR).
- No top-level statements in `src/`. They compile to a `[CompilerGenerated]` class that
  coverage tooling skips, so code there is invisible to the 100% gate. Use an explicit
  `Program.Main`.
- Language-specific code (Roslyn, Java parsers) lives only in `src/Equiv.Frontend.*`.
  Solver-specific code lives only in `src/Equiv.Verify.*`. `Equiv.Core` references neither.
- Names: `Equiv.<Component>`. IR types are prefixed `Ir` (`IrProcedure`, `IrBlock`).
- Commit messages: Conventional Commits (`feat(frontend-csharp): ...`), one ticket id in the
  footer (`Ticket: M1-003`).

## Environment notes

- Windows dev box. Legacy `.csproj` loading needs VS 2026 Build Tools + .NET Framework 4.8
  targeting pack (see README). The engine itself is cross-platform.
- The git default branch is `main` (renamed from `master` in M0-001). One branch per ticket, merged by PR.
- Never commit `samples/**/bin`, `obj`, `TestResults`, `StrykerOutput`.
- Never filesystem-search for `Sarif.Sdk` (`find`, `Get-ChildItem -Recurse`). It ships as
  `Sarif.dll`/`Sarif.xml` in namespace `Microsoft.CodeAnalysis.Sarif` — nothing on disk is
  called `Sarif.Sdk.dll`, so the search scans the whole drive and returns nothing, every
  time. It is at `<global-packages>/sarif.sdk/<version>/lib/netstandard2.0/`, and the
  `Sarif.xml` there is the full documented API — read it instead of guessing or writing a
  probe app. Restored packages generally live at `$env:NUGET_PACKAGES` (else
  `$env:USERPROFILE\.nuget\packages`) `/<id lower-cased>/<version>/lib/<tfm>/`, version
  pinned in `Directory.Packages.props`; computing that path beats searching for any
  package. See `.claude/skills/equiv-package-api`.

## Decisions

Do not ask the user about implementation details (representation, naming, API shape,
encoding, test strategy). Decide with `.claude/skills/equiv-decide/SKILL.md`, log one
`Decision:` line in the ticket's Notes, and continue. Ask only for the things that
skill says are not details, and ask by proposing an ADR.

## When stuck

Do not "try things" against the toolchain for more than ~15 minutes. Write what you
observed into the ticket file under `## Notes` and stop with a clear message.
