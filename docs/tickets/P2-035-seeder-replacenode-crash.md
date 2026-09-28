# P2-035 The mechanical seeder crashes on adapters-shortest-paths-dotnet and Git Extensions
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-010

## Goal
M4-007's first real run tried `tools/corpus/corpus.ps1 -SeedMechanical` on
`pmb-tomasjohansson__adapters-shortest-paths-dotnet` and on `gitextensions-8522`, and both crashed with the
same message. The Tomas crash was deterministic (same crash
at `--seed 1` and `--seed 2`, before any random selection even runs — it fails immediately after
`corpus.ps1` prints "weighting toward N changed identities"):

```
Unhandled exception. System.InvalidOperationException: The item specified is not the element of a list.
   at ... tools/corpus/seeder ... (via corpus.ps1:475, `dotnet run --project (Join-Path $PSScriptRoot 'seeder') ...`)
```

"The item specified is not the element of a list" is Roslyn's own message from
`SyntaxNode.ReplaceNode`/`ReplaceNodes` when the node passed is not (or is no longer) part of the
tree being asked to replace it — see `tools/corpus/seeder/MethodSeeder.cs`'s
`candidate.FileRoot.ReplaceNode(candidate.Method, mutated)` (line ~55) and
`tools/corpus/seeder/SyntaxMutator.cs`'s several `ReplaceNode(s)` calls. The likely cause: two
selected candidates land in the same source file, and after the first mutation is applied the
file's `FileRoot`/`Method` node references collected before any mutation (during candidate
collection) go stale for the second, since they still point into the pre-mutation tree.
`pmb-shiningrush__serviceant` and `pmb-lethek__signalr.extras.autofac` did not hit this (13 and 10
seeds applied cleanly), so 2 of the 4 pairs crash. The two that crash are the two with the most
files and the most methods per file, which fits the same-file explanation but is not proof of it.

## Spec references
`tools/corpus/seeder/MethodSeeder.cs`, `tools/corpus/seeder/SyntaxMutator.cs`. This is
`tools/corpus/`, not `src/`/`tests/`, so it's the M4-010 mechanical-seeder tool, not engine code.

## Acceptance criteria (all must hold; nothing beyond them)
1. Reproduce with a small standalone directory containing two candidate methods in the same file.
2. Fix `MethodSeeder` so applying one file's mutations re-parses (or otherwise keeps in sync) after
   each `ReplaceNode`, instead of holding node references from the original, pre-mutation tree.
3. `./tools/corpus/corpus.ps1 -SeedMechanical pmb-tomasjohansson__adapters-shortest-paths-dotnet -Count 300 -Seed 1` and the same for `gitextensions-8522` complete without crashing.

## Size guard
This is a bug in one function's node-reference lifetime; if fixing it needs a redesign of how
`MethodSeeder` collects candidates across files, stop and write an ADR instead.

## Out of scope
Any other seeder limitation not related to same-file multi-candidate mutation.

## Notes
