---
name: equiv-quality-gates
description: Run or debug this repo's quality gates (build, format, tests, 100% coverage, architecture, mutation). Use when a gate fails, when asked whether it is green, or before opening a PR.
---

# Running the gates

Single entry point: `./build.ps1` (PowerShell 7 or 5.1). Flags: `-Integration` (Windows
only; runs the CLI on `samples/`), `-Mutation` (slow; Stryker), `-NoRestore`.

What it runs, in order, and how to read failures:

| Step | Command | If it fails |
|---|---|---|
| restore | `dotnet restore --locked-mode` | lock file drift: run `dotnet restore` without the flag, commit `packages.lock.json` changes, explain in PR |
| build | `dotnet build -c Release -warnaserror --no-restore` | fix the warning; never suppress. If a rule is wrong for the whole repo, set severity in `.editorconfig` with a comment and mention it in the PR |
| format | `dotnet format --verify-no-changes --no-restore` | run `dotnet format` and commit |
| test | `dotnet test --no-build -c Release --coverage --coverage-output-format cobertura` | failing snapshot tests write `*.received.*` files; diff against `*.verified.*`; accept only if the change is intended |
| coverage | `dotnet run --project tools/check-coverage -- TestResults` | prints per-assembly line/branch rates and uncovered lines; write the missing test, do not exclude |
| architecture | inside `dotnet test` (`Equiv.Tests.Architecture`) | you added a forbidden reference; move the code to the right project |
| integration | same test run with `-Integration` | needs VS Build Tools; paste any MSBuildWorkspace diagnostics into the ticket Notes |
| mutation | `dotnet stryker --test-runner mtp` | survivors mean a missing assertion; add the test that kills them |

## Coverage, precisely

- 100% line AND branch, per `src/` assembly. Tests and `tools/` are not measured.
- `[ExcludeFromCodeCoverage(Justification = "M2-001: spawns MSBuild build host, covered by integration tests")]`
  is the only accepted form. The checker rejects any justification without a ticket id.
- Branch coverage counts `??`, `?.`, ternaries and pattern switches. Test every arm.

## Adding a package

Add the version to `Directory.Packages.props`, a row to `docs/adr/0002-dependencies.md`,
run `dotnet restore` to update lock files, and say why in the PR. No other way.
