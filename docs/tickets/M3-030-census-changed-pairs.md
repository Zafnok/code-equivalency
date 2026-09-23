# M3-030 Census reports changed pairs, their reason sets, runtime-change calls and package drift
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-014, ADR 0034 accepted

## Goal
ADR 0034. Today the census counts every matched pair, including byte-identical ones the solver
never sees, and counts bodies per reason instead of reason sets per pair. After this ticket, the
census also reports the changed pairs, the reason set of each, how often a runtime-changes member
is called, and, in the corpus tooling, which NuGet packages changed version. That is what the Git
Extensions census rerun needs in order to decide feasibility and to order M4 exactly.

## Spec references
ADR 0034; ADR 0028 decision 5; ADR 0027; VERIFICATION-MODEL.md census paragraph;
`src/Equiv.Cli/LoweringCensus.cs`; `RuntimeChangeTable.TryMatch`; `tools/corpus/corpus.ps1`;
`.claude/skills/equiv-corpus-run/SKILL.md` sections 6 and 7.

## Acceptance criteria (all must hold; nothing beyond them)
1. A matched pair is *changed* unless its two bodies' syntax token sequences are equal once
   trivia is ignored. `run.properties.loweringCensus` gains `changedPairs`,
   `changedPairsWithoutOpaque` and `changedPairsWholeBodyOpaque`, unit-tested on a pair set that
   has both identical and changed bodies.
2. `loweringCensus.changedReasonSets` maps the sorted, `+`-joined union of both sides' opaque
   reasons to a count of changed pairs, with `""` for no opaque. The counts sum to `changedPairs`.
   Unit-tested.
3. `loweringCensus.runtimeChangeCalls` has, per side, `callSites`, `distinctMembers` and
   `pairsWithAny` over all matched pairs, counted with `RuntimeChangeTable.TryMatch` on each
   `IrCall`'s identity. Unit-tested with a table member and a non-member.
4. `business-layer`'s census snapshot is updated, and the diff shows only the new keys.
5. `corpus.ps1 -Packages <slug>` prints the packages whose resolved version differs between the
   sides, and those on one side only. It reads `project.assets.json` and `packages.config` and never
   prints source text. `corpus.ps1 -Metrics` prints the new census keys.
6. The skill's SUMMARY.md template gains a `## Changed code` section (changed pairs, lowerable
   share over them, the top reason sets, runtime-change calls, package changes). Its verdict step
   applies ADR 0034's evaluation paragraph. VERIFICATION-MODEL's census paragraph defines the
   new keys.

## Size guard
Census counting in `Equiv.Cli` plus the token comparison in the frontend, and `corpus.ps1`. If this
needs a change to lowering or to the matcher, stop.

## Out of scope
M3-015's bound fingerprints (they replace the token proxy when they land). Reporting package drift
as a SARIF result. Rerunning the census; that is its own run.

## Notes
