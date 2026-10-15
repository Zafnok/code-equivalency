# P2-197 Precision bug: OpenRA's mod assemblies count as unhosted, so a .NET Framework to .NET 5 pair gets the .NET 9 float-to-integer rule
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-181's count of the bodies a runtime rule keeps out of congruence (equiv `c6d5f751`, compare mode
quick; its Notes) found 69 of `openra-17989`'s 242 under the built-in `float-to-integer` rule, which
ADR 0040 places at `net9.0`. The pair is net472 against net5.0, which does not cross .NET 9. The rule
applies because four of the modern side's projects (`OpenRA.Mods.Cnc`, `OpenRA.Mods.Common`,
`OpenRA.Mods.D2k`, `OpenRA.Platforms.Default`) target `netstandard2.1` and no project references them:
the game loads them as plugins at run time. `run.properties.runtimes` gives them source `unhosted`,
and ADR 0040 decision 1 has an unhosted pair cross every change the table covers, up to .NET 10.

The 69 are 31 `timeout`, 26 `abstraction`, 8 `opaque` and 4 `unaligned-loop` Unknowns. Their other
rows, if any, still apply between net472 and net5.0.

When done, the corpus run of `openra-17989` gives those four projects the runtime of the process that
loads them, so no rule changed after .NET 5 applies to them.

## Spec references
ADR 0040 decision 1 (`equiv.json` `runtimes.legacy` and `runtimes.modern`), `tools/corpus/README.md`,
`.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `openra-17989`'s run states the modern side's runtime as ADR 0040 decision 1 allows (`equiv.json`,
   written by `corpus.ps1` for the pair, or the pair's own config), with the source that shows the game
   hosts the mods on .NET 5.
2. Rerun `openra-17989` in compare mode quick; Notes give the `runtime-rule` bodies whose identity
   names `float-to-integer`, next to 69.

## Tests
A `corpus.ps1` test, or the existing loader test of `runtimes`, that a stated runtime reaches an
unhosted `netstandard` project.

## Size guard
Detecting a plugin host from code (an `Assembly.Load` of a path): stop, that is an ADR 0040 change.

## Out of scope
Other pairs' unhosted projects.

## Notes
- Filed 2026-10-14 by P2-181 (criterion 1): the third-largest rule its count found, and none of its
  rows.
- Decision: the stated runtime is the pair's own config, committed as `tools/corpus/configs/<slug>.json`
  and passed by `-Compare` as `--config` (unless `-CompareArgs` names one), not an `equiv.json` that
  `corpus.ps1` writes into the run. A committed file can be reviewed, and its source goes in
  `tools/corpus/README.md`'s "Pair configs", since the config loader takes no comment key. Only
  `runtimes.modern` is stated: every legacy project names `net472`.
- Criterion 1: `tools/corpus/configs/openra-17989.json` states `runtimes.modern` `net5.0`. The source
  (README "Pair configs"): the four projects target `netstandard2.1` and nothing references them.
  Outside a Mono build, `OpenRA.Game` loads them by path through the pair's new `AssemblyLoader` (an
  `AssemblyLoadContext`): `ObjectCreator.cs` loads the mods and `Game.cs` the renderer. `OpenRA.Game`
  runs in `OpenRA.Launcher`, `OpenRA.Server` and `OpenRA.Utility`, which target `net5.0` outside Mono,
  and in `OpenRA.WindowsLauncher`, which targets `net5.0`. In the run below, `run.properties.runtimes.modern`
  gives all four `net5.0`, source `config`, where they were `netstandard2.1`, source `unhosted`.
  The loader already had a test for a stated runtime reaching an unhosted project
  (`RuntimeDetectionTests`). This PR adds the `corpus.ps1` side in `tools/corpus/tests/Compare.Tests.ps1`:
  `Compare_PassesThePairsOwnConfig`, `Compare_ACommandLineConfigReplacesThePairs` and
  `PairConfig_OpenRaStatesItsModsRunOnNet5` (8/8 pass).
- Criterion 2: 2026-10-15, both runs with the same Release build of this branch (`main` `7dc353e8` plus
  this change), compare mode quick, `openra-17989`, each exit 1. The baseline passes an empty config
  through `-CompareArgs '--config'`, which is what `main` does.

  | Run | `runtime-rule` bodies | ... naming `float-to-integer` | EQ001 | EQ003 | EQ006 | EQ002 | Time |
  |---|---|---|---|---|---|---|---|
  | baseline (`20261015-0302-full-quick-main`) | 225 | 69 (31 timeout, 26 abstraction, 8 opaque, 4 unaligned-loop) | 9,852 | 231 | 37 | 0 | 230 s |
  | this PR (`20261015-0258-full-quick-p2-197`) | 153 | **0** (was 69) | 9,924 | 162 | 34 | 0 | 177 s |

  The 69 bodies now: 52 Equivalent, and 17 still Unknown because another row of theirs applies
  between net472 and net5.0 (9 timeout, 3 abstraction, 3 opaque, 2 unaligned-loop). Every one of the
  17 still has a `runtime-rule` component.
  Also moved, all because the mods no longer cross every change: 17 more Unknowns became Equivalent,
  and 3 EQ006 on `OpenRA.Mods.Common.Traits.Harvester` (`get_Fullness`, `get_IsFull`, `get_IsEmpty`)
  became Equivalent. Those three were the vectorized `Enumerable.Sum` row, which changed after .NET 5.
  Their messages said "between net40 and net10.0", the whole range the table covers, which is what an
  unhosted pair crossed. They were false positives under ADR 0040's interval rule. No result moved the
  other way.
  No new finding, so this PR files no ticket.
