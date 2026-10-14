# P2-191 A library project that a host loads at run time is judged against every runtime the table covers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
ADR 0040 (P2-055) gives an unhosted project the widest runtime interval: "any other unhosted pair counts
as crossing every change the table covers". On `openra-17989` the four mod projects
(`OpenRA.Mods.Common`, `OpenRA.Mods.Cnc`, `OpenRA.Mods.D2k`, `OpenRA.Platforms.Default`) target
`netstandard2.1` and no project references them: the net5.0 host loads them at run time. Their EQ006
messages say "between net40 and net10.0", and 17 of the run's 48 EQ006 are in them. P2-130's
adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 1 false EQ006 that only this causes:
`OpenRA.Mods.Common.Traits.Harvester::get_Fullness()`, under the `Enumerable.Sum` row, `changedIn`
net8.0, beyond the pair's modern runtime.

When done, a pair can state the runtime such a project runs on, or it is inferred, and the interval is
the pair's real one.

## Spec references
ADR 0040 and P2-055, `run.properties.runtimes`, the CLI's options, `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, through `equiv-adr`, how an unhosted library gets its runtime: an option or configuration
   entry, the solution's only host's runtime, or both.
2. Implement it, with a sample of an unhosted `netstandard` library beside a net5.0 host.
3. Rerun `openra-17989` in compare mode quick; Notes give the EQ006 count in the four projects next to
   17, and `Harvester::get_Fullness()`'s new result.

## Tests
Runtime detection unit tests; the sample.

## Size guard
Loading the host's dependency graph from built output: stop.

## Out of scope
Runtime rows themselves.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
