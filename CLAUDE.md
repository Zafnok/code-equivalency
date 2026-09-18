# CLAUDE.md — rules for working in this repo

You are implementing a plan that has already been decided. Do not re-architect. If a
decision looks wrong, write a short ADR proposing the change (see `.claude/skills/equiv-adr`)
and stop; do not silently deviate.

## How work is picked up

- Work is defined in `docs/tickets/`. One ticket = one PR. Do the ticket, nothing more.
- Follow `.claude/skills/equiv-task-loop/SKILL.md` for every ticket. It is the definition of done.
- Milestones and ordering are in `docs/ROADMAP.md`. Do not start a ticket whose
  dependencies are not merged.

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
  `internal` by default, `InternalsVisibleTo` for the matching test project only.
- No reflection, no dynamic, no `#pragma warning disable` (use `.editorconfig` severity
  with a comment if a rule is genuinely wrong for this repo, and mention it in the PR).
- Language-specific code (Roslyn, Java parsers) lives only in `src/Equiv.Frontend.*`.
  Solver-specific code lives only in `src/Equiv.Verify.*`. `Equiv.Core` references neither.
- Names: `Equiv.<Component>`. IR types are prefixed `Ir` (`IrProcedure`, `IrBlock`).
- Commit messages: Conventional Commits (`feat(frontend-csharp): ...`), one ticket id in the
  footer (`Ticket: M1-003`).

## Environment notes

- Windows dev box. Legacy `.csproj` loading needs VS 2026 Build Tools + .NET Framework 4.8
  targeting pack (see README). The engine itself is cross-platform.
- The git default branch is `main`. The local branch was created as `master`; M0-001 fixes it.
- Never commit `samples/**/bin`, `obj`, `TestResults`, `StrykerOutput`.

## When stuck

Do not "try things" against the toolchain for more than ~15 minutes. Write what you
observed into the ticket file under `## Notes` and stop with a clear message.
