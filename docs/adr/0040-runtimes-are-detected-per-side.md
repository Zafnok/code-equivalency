# ADR 0040: Each side's runtime is detected from its projects, and a runtime rule applies only if the pair crosses it

Status: accepted (2026-09-28). Supersedes in part ADR 0024 (what makes a body runtime-sensitive),
ADR 0025 (which pure functions are side-specific) and ADR 0035 (which runtimes execution uses), and
extends ADR 0028 decision 3 with a fourth kind of pair.

## Context
`equiv` assumes that `--legacy` runs on .NET Framework 4.8 and `--modern` on .NET 10, and nothing
in `src/` reads either side's target framework:
- all 367 `runtime-changes.json` rows apply to every call on both sides;
- float-to-integer conversion is always runtime-sensitive;
- `--execute` always runs the legacy side on .NET Framework 4.8 (`DriverFactory`) and the modern side on .NET 10.

A same-runtime commit is therefore mis-analysed. The 2026-09-28 success assessment found no
public C# cleanup benchmark, but it found real "no functional change" PRs (Git Extensions #11372
and #11284, PowerShell #19687), all net8.0 on both sides. On such a pair:
- about 735 of Git Extensions' changed pairs would lose congruence for no reason (the P1-011 spike
  counts them);
- EQ006 would cite ICU or IEEE formatting changes that both sides share;
- replay would start a net8.0 assembly on the .NET Framework CLR.

A migration from .NET 6 to .NET 8 would get rows for changes both sides already have.

## Decision
`equiv` compares any two C# solutions whose projects target .NET Framework 4.x or .NET (Core) 3.0
or later: a framework migration, a version upgrade, or a commit that keeps the runtime (a cleanup).
The runtime is a fact read from each side, never an assumption.
1. **Detection.** Each loaded project gets a runtime, `(family, version)`: `.NETFramework` 4.x or
   `.NETCoreApp` n.m. It is read from the compilation's `TargetFrameworkAttribute`, using the
   flavour P2-016 already analyses for a multi-targeted project.
   - A `netstandard` project runs on its host. Its runtime is that of the executable and test
     projects that reference it, directly or transitively, on the same side.
   - If a side has no such host, `equiv.json` can state it (`runtimes.legacy`, `runtimes.modern`).
     Otherwise two unhosted `netstandard` projects with equal target frameworks count as the same
     runtime, and any other unhosted pair counts as crossing every change the table covers.
   - `run.properties.runtimes` lists each side's projects with their runtime and where it came from.
2. **Interval.** A matched pair crosses the runtimes between its legacy project's runtime and its
   modern project's runtime, in either order. .NET Framework orders before every .NET (Core)
   version.
   - Every runtime rule has a change point. A `runtime-changes.json` row gets `changedIn`. The
     built-in rules get theirs too: float-to-integer saturation at `net9.0`, and x87 arithmetic
     wherever exactly one side runs on the 32-bit .NET Framework JIT.
   - A rule applies to a pair only if its change point lies inside the pair's interval. A row with
     no known change point applies whenever the runtimes differ.
   - For a same-runtime pair no rule applies. EQ006 cannot fire there, and runtime sensitivity does
     not block congruence.
   - A pair whose interval reaches outside the table's documented coverage gets one run-level
     notification naming the uncovered range.
3. **Execution.** `--execute` and `tools/runtime-diff` run each side on its detected runtime:
   - an `.exe` with an `app.config` for .NET Framework;
   - a `.dll` with a `runtimeconfig.json` for its own .NET version, with no roll-forward.

   A runtime that is not installed makes execution unavailable for that side, with the reason
   recorded. Another runtime is never used in its place. Windows is required only when a side
   runs on .NET Framework.
4. **Names.** `--legacy` and `--modern` stay and mean before and after. `--before` and `--after`
   are aliases, and SARIF keeps `legacy` and `modern`.
5. **Corpus.** `tools/corpus/pairs.csv` gains the kind `cleanup`: a public before-and-after whose
   author states that it changes no behaviour, on any runtimes.
   - Cleanup pairs are reported in their own section of the verdict file: changed pairs, the
     solver-proved share of them, and every Divergent adjudicated as in P2-047.
   - They take no part in ADR 0028's thresholds, which are about migrations and stay as written.

## Why
- The runtime is in the project file. The assumption is right only for 4.8 against 10. On every
  other pair it flags too much, which is safe but makes the results useless. Execution on the wrong
  runtime can also report an observed Divergent that the real runtimes would not show.
- Change points are already known. 367 rows cite a versioned Microsoft compatibility page
  (`compatibility/3.0`, `5.0` … `10.0`, `fx-core`, `unsupported-apis`), so backfilling `changedIn` is
  mechanical.
- Same-runtime commits are where congruence is strongest, because the runtime cannot change a
  byte-identical body. The corpus today cannot show that, because every pair crosses Framework to
  Core.
- Execution on a runtime the code does not target measures the wrong thing, which defeats
  ADR 0035's purpose.

## Rejected
- **A `--mode cleanup` switch that turns runtime rules off.** It is right only for same-runtime
  pairs, misses version upgrades (net6 to net8), and a wrong switch is silent.
- **One runtime per side, not per project.** A migrated solution can mix `net48`, `netstandard2.0` and
  `net8.0`, so a per-side runtime is wrong for part of every such solution.
- **Renaming `--legacy`/`--modern` and the SARIF fields.** That breaks every existing baseline and
  caller, and the aliases cover the wording.
- **Scoring cleanup pairs with ADR 0028's thresholds.** The unchanged-share rule means "the migration
  is a rewrite" and has no meaning for a commit that is meant to change code.

## Consequences
- Tickets P2-053 to P2-058 implement it: detection, `changedIn`, applying the interval, execution,
  the CLI and docs, and the cleanup pairs.
- ADR 0024, 0025 and 0035 carry "superseded in part by 0040" in their Status lines from
  acceptance.
- As the tickets land, each changes the text that describes its behaviour:
  - README's scope line;
  - `docs/VERIFICATION-MODEL.md` sections on runtime sensitivity, EQ006 and execution;
  - `docs/ARCHITECTURE.md`'s loader and execution notes;
  - the header text of `runtime-changes.json` and `api-equivalences.json`.
- `api-equivalences.json` is unchanged. Its entries equate two members that behave the same on any
  runtime that has both, and it keeps being applied to the before side.
- Verdicts on existing 4.8-to-10 runs do not change, because every row's change point lies inside
  that interval. Runs on other runtime pairs change, and their baselines show those results as
  `new`.
- The loader needs nothing new. SDK-style projects on either side already load through
  MSBuildWorkspace, and the bare loader stays .NET Framework only.
