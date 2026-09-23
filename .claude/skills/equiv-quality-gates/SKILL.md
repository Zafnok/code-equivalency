---
name: equiv-quality-gates
description: Run or debug this repo's quality gates (build, format, tests, 100% coverage, architecture, mutation). Use when a gate fails, when asked whether it is green, or before opening a PR.
---

# Running the gates

Single entry point: `./build.ps1` (Windows PowerShell 5.1 or PowerShell 7). One flag:
`-Integration` (Windows only; also runs `Equiv.Tests.Integration`, which needs VS Build
Tools and the 4.8 targeting pack). Debug configuration on purpose: Release inlining
distorts branch coverage.

What it runs, in order, and how to read failures:

| Step | Command | If it fails |
|---|---|---|
| restore | `dotnet restore --locked-mode` | lock file drift: run `dotnet restore` without the flag, commit `packages.lock.json` changes, explain in PR |
| build | `dotnet build --no-restore -warnaserror` | fix the warning; never suppress. If a rule is wrong for the whole repo, set severity in `.editorconfig` with a comment and mention it in the PR |
| format | `dotnet format --no-restore --verify-no-changes` | run `dotnet format` and commit |
| test | per test project: `dotnet test <proj> --no-restore --no-build -- --coverlet --coverlet-output-format cobertura` (coverlet.MTP flags go after `--`) | failing snapshot tests write `*.received.*` files; diff against `*.verified.*`; accept only if the change is intended |
| check-coverage | `dotnet run --project tools/check-coverage -- --test-results TestResults --src src` (also written to `TestResults/coverage-summary.txt`) | prints per-assembly line/branch rates; write the missing test, do not exclude |
| architecture | inside the test step (`Equiv.Tests.Architecture`) | you added a forbidden reference; move the code to the right project |
| integration | same test step with `-Integration` | paste any MSBuildWorkspace diagnostics into the ticket Notes |
| mutation (CI only, not in build.ps1) | from `tests/<X>.Tests`: `dotnet stryker --test-runner mtp --project <X>.csproj --target-framework net10.0` | survivors mean a missing assertion; add the test that kills them |

## Coverage, precisely

- 100% line AND branch, per `src/` assembly. Tests and `tools/` are not measured.
- Reports from all test projects are merged per assembly (max hits per line), because
  each test project only exercises part of a shared assembly.
- `[ExcludeFromCodeCoverage(Justification = "M2-001: spawns MSBuild build host, covered by integration tests")]`
  is the only accepted form. The checker rejects any justification without a ticket id.
- Branch coverage counts `??`, `?.`, ternaries and pattern switches. Test every arm.
- Zero coverable lines reads as 100%. That is fine for placeholders and wrong for real
  code: top-level statements and other compiler-generated types are skipped by coverlet,
  which is why CLAUDE.md forbids them in `src/`.

## Adding a package

Add the version to `Directory.Packages.props`, a row to `docs/adr/0002-dependencies.md`,
run `dotnet restore` to update lock files, and say why in the PR. No other way.
