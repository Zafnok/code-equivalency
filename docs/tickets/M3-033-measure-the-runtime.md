# M3-033 Measure the runtime: every BCL member the corpus calls, run on both runtimes, and measured rows for the ones that differ
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-032, M3-030

## Goal
The census reports calls to members that are already in the table (`runtimeChangeCalls`, ADR
0034). It says nothing about the members that are not in it. This ticket makes the census list
every BCL member a lowered body calls, then runs `tools/runtime-diff` on the most-called ones
from the corpus pairs. A member that differs becomes a row with `source: measured` and a witness.
A member that agrees is recorded as tested, with its case count, in the run summary only. This
turns "Equivalent by congruence" on a retarget from "no listed member is called" into "no listed
member is called, and the BCL members it calls were measured".

## Spec references
ADR 0035 decision 1; ADR 0034 (census fields); ADR 0028 (corpus rules: only corpus code, nothing
third-party committed); `docs/runs/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `loweringCensus` gains `externalCallees`: for each side, the distinct `CallIdentity` values of
   calls whose target assembly is part of the runtime (the framework reference assemblies the
   project compiled against), each with its call-site count. They are sorted by count, then
   ordinally. `--lower-only` writes them. The census snapshot on `business-layer` shows the field.
2. `tools/corpus/corpus.ps1 -RuntimeDiff <slug> [-Top 200]` reads a pair's census SARIF, takes
   the union of both sides' top N `externalCallees`, runs `runtime-diff` on each, and writes the
   reports under `.corpus/runs/<slug>/runtime-diff/`. Nothing is written outside `.corpus/`.
3. The run covers Git Extensions and the three agent pairs from the 2026-09-24 census. Its
   `docs/runs/<date>-runtime-diff/SUMMARY.md` has:
   - members run, divergent, nondeterministic on one side, and not constructible;
   - per divergent member: its identity, the cultures it diverged under, and whether a curated
     or documented row already covered it.

   It holds no inputs taken from corpus code: witnesses are generated inputs, so they may appear.
4. Every divergent member that no existing row covers becomes a `runtime-changes.json` row with
   `source: measured`, a reason in your own words, and `witness`: the input, culture and both
   canonical outcomes. `docs/runtime-changes-review.md` gains a "Measured" section listing them.
5. A member that diverges only in `Nondeterministic` on the modern side gets a row too, with
   reason `nondeterministic on .NET 10 only`.
6. The PR description states how many congruent Git Extensions pairs lose congruence because of
   the new rows, using M3-030's token-identical proxy. That is the number of former silent
   Equivalents on the human pair.

## Files
`src/Equiv.Cli/LoweringCensus.cs` (and whatever it needs in `src/Equiv.Frontend.CSharp` to know a
call's target assembly), `tools/corpus/corpus.ps1`, `tools/corpus/README.md`,
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, `docs/runtime-changes-review.md`,
`docs/runs/<date>-runtime-diff/SUMMARY.md`, census snapshot files, tests in the matching projects.

## Tests
`Census_ListsExternalCalleesByCount`, `Census_ExcludesCallsIntoTheSolution`,
`RuntimeChangeTableTests.MeasuredRowHasAWitness`.

## Size guard
Any change to `runtime-diff`'s engine means M3-032 missed something. File it as a P2 ticket and
stop.

## Out of scope
Members with non-constructible parameters (list them, do not extend the generators). Third-party
NuGet packages' members: package drift is ADR 0034's `packageVersionChanges`, and executing
packages is later work.

## Notes
