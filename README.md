# equiv — behavioural equivalence checker for migrated code

`equiv` proves (or refutes, with a counterexample) that a program behaves the same
before and after a migration. It does not diff syntax. It lowers both versions to a
language-neutral intermediate representation (IR), encodes matched procedure pairs as
SMT problems, and asks Z3 whether any input can make them disagree.

MVP scope: **.NET Framework 4.8 → .NET 10 (C#)**. Later: Java 11 → 25, then
cross-language rewrites. The language frontends are the only language-specific parts.

Status: **planning complete, no code yet.** Start at [docs/ROADMAP.md](docs/ROADMAP.md).

## Read in this order

1. [CLAUDE.md](CLAUDE.md) — rules for anyone (human or agent) touching this repo.
2. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — components, boundaries, data flow.
3. [docs/VERIFICATION-MODEL.md](docs/VERIFICATION-MODEL.md) — what "equivalent" means here, exactly.
4. [docs/QUALITY-GATES.md](docs/QUALITY-GATES.md) — the gates every change must pass.
5. [docs/ROADMAP.md](docs/ROADMAP.md) — milestones; [docs/tickets/](docs/tickets/) — the work items.
6. [docs/adr/](docs/adr/) — why each technology was chosen (and what was rejected).

## Layout

```
src/        production code, one project per component (see ARCHITECTURE.md)
tests/      one test project per src project, plus tests/Equiv.Tests.Integration
samples/    tiny paired legacy/modern solutions used as fixtures and demos
docs/       everything above
.claude/    skills that encode the workflow for coding agents
```

## Prerequisites (Windows dev box, MVP)

- .NET 10 SDK (latest patch).
- Visual Studio 2026 **Build Tools** with workload ".NET desktop build tools" and the
  component ".NET Framework 4.8 targeting pack". This is what lets Roslyn's out-of-process
  build host evaluate legacy (non-SDK) `.csproj` files. No Win32 API is used anywhere.
- Git, Docker Desktop (for the container packaging milestone), GitHub CLI.

None of these were installed on the box at planning time. Ticket M0-001 installs them.
