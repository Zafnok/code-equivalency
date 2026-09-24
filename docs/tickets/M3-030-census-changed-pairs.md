# M3-030 Census reports changed pairs, their reason sets, runtime-change calls and package drift
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-014

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
   trivia is ignored and neither lowered body has an `IrCall` that `RuntimeChangeTable.TryMatch`
   matches. `run.properties.loweringCensus` gains `changedPairs`, `changedPairsWithoutOpaque` and
   `changedPairsWholeBodyOpaque`, unit-tested on a pair set that has identical bodies, changed
   bodies, and identical bodies calling a runtime-changes member (counted as changed).
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
   applies ADR 0034's evaluation paragraph and reports `1 - changedPairs / matchedPairs` next to
   ADR 0028's unchanged share, labelled as the pair-level figure. VERIFICATION-MODEL's census
   paragraph defines the new keys.

## Size guard
Census counting in `Equiv.Cli` plus the token comparison in the frontend, and `corpus.ps1`. If this
needs a change to lowering or to the matcher, stop.

## Out of scope
M3-015's bound fingerprints (they replace the token proxy when they land). Reporting package drift
as a SARIF result. Rerunning the census; that is its own run.

## Notes
- Decision: the token proxy compares the tokens (kind and text) of every declaring syntax reference of the
  two method symbols, so a signature change counts too, and it is computed in `CSharpFrontend` onto a new
  `ProcedurePair.TokensEqual` (default false: a frontend that does not compute it reports every pair changed,
  the conservative direction). An implicit member (no declaration) has no tokens on either side.
- Decision: `runtimeChangeCalls` is `{callSites, distinctMembers, pairsWithAny}`, each `{legacy, modern}`,
  matching the census's other per-side leaves. `distinctMembers` counts distinct callee `CallIdentity.Value`s,
  not table rows. The match uses the unsuppressed `TryMatch`, so the census measures exposure whatever
  `suppressRuntimeChanges` says.
- Decision: `changedReasonSets` is left out of Verify snapshots when it is empty (Verify drops empty
  dictionaries), as `opaqueByReason` already is.
- The `""` key of `changedReasonSets` is not a valid property name for `ConvertFrom-Json` without
  `-AsHashtable`, which Windows PowerShell 5.1 lacks. `-Metrics` rewrites that one key to `(no opaque)`
  before parsing.
- `Format-Table | Out-Host` printed nothing under pwsh on Linux with stdout redirected; `-Packages` uses
  `Out-String` through `Show-Step` instead. The other switches' `Out-Host` tables were left alone (out of scope).
- Linux dev box: the business-layer legacy sample loads only with net48 reference assemblies on
  `TargetFrameworkRootPath` (restored `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3, as `-Prepare`
  does). `ComparePipelineTests.AddedAndRemovedHaveLocations` then also differs in `file:///` URI prefixes on
  Linux; only the census hunk of its snapshot was taken.
- Business-layer census: 4 of 28 matched pairs changed, 1 of them without opaque (lowerable share 25%), no
  runtime-change calls.
