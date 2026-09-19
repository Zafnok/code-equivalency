[![Quality gate status](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)

# equiv — behavioural equivalence checker for migrated code

`equiv` proves (or refutes, with a counterexample) that a program behaves the same
before and after a migration. It does not diff syntax. It lowers both versions to a
language-neutral intermediate representation (IR), encodes matched procedure pairs as
SMT problems, and asks Z3 whether any input can make them disagree.

MVP scope: **.NET Framework 4.8 → .NET 10 (C#)**. Later: Java 11 → 25, then
cross-language rewrites. The language frontends are the only language-specific parts.

## Status

Milestones M0 (skeleton and gates) and M1 (core IR, samples, SARIF, CLI shell) are
merged. The pipeline exists end to end in `Equiv.Core` and `Equiv.Cli`, but no language
frontend or solver backend is plugged in yet, so `equiv compare` on a real solution
reports "no frontend" and exits 3. Next up is M2 (the C# frontend), starting with
M2-001. Progress and carried-forward items: [docs/ROADMAP.md](docs/ROADMAP.md).

What exists today:

- `Equiv.Core` — SSA IR (records, validator, text dump/parse round trip, interpreter),
  CsCheck generators in `tests/Equiv.TestSupport`; verdict model (`Equivalent`,
  `Divergent` + counterexample, `Unknown` + reason, `Added`, `Removed`); procedure
  identity normalisation with rename maps; `equiv.config.json` loader; stable identity
  matcher; SARIF 2.1.0 writer with fingerprint-based `baselineState`.
- `Equiv.Cli` — `equiv compare` argument parsing, frontend routing by language, exit
  codes, `--dry-run`, `--baseline`, `--fail-on`.
- `Equiv.Frontend.CSharp`, `Equiv.Verify.Z3` — empty shells until M2 and M3.
- `samples/` — five paired 4.8/10 solutions, each README stating the expected verdicts.
- Gates — 100% line and branch coverage on every `src/` project, warnings as errors,
  ArchUnitNET dependency rules, CodeQL, gitleaks, Dependabot, locked restores. Stryker
  and SonarQube Cloud run on every PR but are not yet required checks.

## Usage (current surface)

```
equiv compare --legacy <path> --modern <path>
              [--out equiv.sarif] [--baseline <previous.sarif>]
              [--config equiv.config.json] [--fail-on divergent|unknown] [--dry-run]
```

| Exit code | Meaning |
|---|---|
| 0 | No new `Divergent` results (and no new `Unknown` when `--fail-on unknown`) |
| 1 | At least one `Divergent` result that is `new` relative to `--baseline` |
| 2 | `--fail-on unknown` and at least one new `Unknown` result |
| 3 | Usage error: bad arguments, missing or unparseable file, no frontend for the paths |
| 4 | A frontend failed to load a solution |

Output is always SARIF 2.1.0 (rules EQ001 to EQ006); the exact meaning of each verdict
and of `baselineState` is in [docs/VERIFICATION-MODEL.md](docs/VERIFICATION-MODEL.md).

## Building and running the gates

```
./build.ps1               # build, format check, tests, 100% coverage gate, architecture tests
./build.ps1 -Integration  # also runs tests/Equiv.Tests.Integration (needs VS Build Tools)
```

Every gate in [docs/QUALITY-GATES.md](docs/QUALITY-GATES.md) runs locally through this
script and in GitHub Actions on every PR. Coverage flags for coverlet.MTP go after `--`
on `dotnet test`; the script already does this.

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
tests/      one test project per src project, Equiv.TestSupport (generators),
            Equiv.Tests.Architecture, Equiv.Tests.Integration
samples/    tiny paired legacy/modern solutions used as fixtures and demos
tools/      check-coverage: merges cobertura reports and enforces the 100% gate
docs/       everything above
.github/    ci.yml, codeql.yml, mutation.yml, sonar.yml, dependabot.yml
.claude/    skills that encode the workflow for coding agents
```

## Prerequisites (Windows dev box, MVP)

- .NET 10 SDK (the exact patch is pinned in `global.json`).
- Visual Studio 2026 **Build Tools** with workload ".NET desktop build tools" and the
  component ".NET Framework 4.8 targeting pack". This is what lets Roslyn's out-of-process
  build host evaluate legacy (non-SDK) `.csproj` files. No Win32 API is used anywhere.
  Build Tools install under `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools`.
- Git, Docker Desktop (for the container packaging milestone), GitHub CLI.

The engine itself is cross-platform; only loading legacy `.csproj` files needs Windows
until the bare loader (post-MVP) exists. CI runs the full pipeline on `windows-latest`
and everything except the integration tests on `ubuntu-latest`.
