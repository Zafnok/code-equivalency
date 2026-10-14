# P2-197 Precision bug: OpenRA's mod assemblies count as unhosted, so a .NET Framework to .NET 5 pair gets the .NET 9 float-to-integer rule
Status: todo
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
