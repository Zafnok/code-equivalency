# M0-005 Gate hardening from the M0 review
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-004

## Goal
Close four holes found reviewing M0-002 to M0-004. None changes the design; all make the
100% gate mean what QUALITY-GATES.md says it means.

## Deliverables
- [x] `build.ps1` deletes `TestResults/` before the test step. Today reports from earlier
      runs accumulate (timestamped filenames) and `check-coverage` merges them with max-hits,
      so a line covered by a test that has since been deleted still counts locally.
- [x] `check-coverage` fails when any `src/` assembly discovered by `SrcAssemblyDiscovery`
      has no coverage data at all (today it passes as long as at least one has data).
      Unit test: four src names, three reports, exit 1 naming the missing one.
- [x] `check-coverage` report table shows `covered/valid` counts next to each rate so a
      `0/0 = 100%` placeholder is visible to a reviewer.
- [x] `src/Equiv.Cli/Program.cs` becomes an explicit `internal static class Program` with
      `static int Main(string[] args)`, covered by a test in `Equiv.Cli.Tests`. Top-level
      statements compile to a `[CompilerGenerated]` class that coverlet skips, so today the
      CLI entry point is invisible to the gate (its cobertura report has zero classes).
      CLAUDE.md now forbids top-level statements in `src/`.
- [x] `.editorconfig`: `insert_final_newline = true` for `[*.cs]`; run `dotnet format`.

## Out of scope
Anything in M1.

## Notes
- Flipping `insert_final_newline` touched every `.cs` file in the repo (trailing newline
  only) — the whole tree was missing it, not just the files this ticket owns.
- `check-coverage`'s report table columns got wider to fit `covered/valid` counts; format
  is `NNN/NNN  NN.NN%` per column, plus a `NO COVERAGE DATA` row for assemblies with zero
  reports.
- No toolchain surprises; `./build.ps1` was green on the first full run after each
  deliverable.
