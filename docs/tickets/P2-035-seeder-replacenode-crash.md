# P2-035 The mechanical seeder crashes on adapters-shortest-paths-dotnet and Git Extensions
Status: in-progress
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
- Root cause is not the same-file hypothesis in Goal: `MethodSeeder` already seeds at most one method per file
  (`seededFiles`), so no candidate ever sees a stale tree. The crash is in `SyntaxMutator`: `IntroduceTemporary` /
  `InlineTemporary` call `ReplaceNode(statement, [declare, rewritten])`, which Roslyn only allows on an element of a
  statement list. On an embedded statement (`if (c) return a + b;`, `else x = y;`, a loop or `using` body) it throws
  "The item specified is not the element of a list". It is deterministic because site discovery doesn't depend on the
  seed, and any file with a brace-less `if` has one.
- Repro (criterion 1): a scratch directory with one file `Two.cs` holding two methods, `int First(int a, int b)
  { if (a > b) return a + b; return b; }` and `void Second(int a) { if (a > 0) field = a; else field = 1; }`. `dotnet run
  --project tools/corpus/seeder -- --root <dir> --manifest m.json --count 5 --seed n` crashed at seeds 1 to 3 with the
  message above, and at seed 8 with a second crash of the same kind: `ArgumentNullException (Parameter 'statement') at
  CSharpSyntaxRewriter.VisitElseClause`, from `DropFieldWrite`'s `RemoveNode` on the embedded `else field = 1;`.
  `DropNullCheck`'s `ReplaceNode(branch, <statement list>)` has the same shape on `else if (x == null) ...`. With the fix,
  that directory plus a third method (`else if (x == null) return 2;`, `while (..) o = x;`, a switch section) seeds
  cleanly at seeds 1 to 40.
- Deviation: the fix is in `SyntaxMutator`, not `MethodSeeder` (criterion 2 names the latter based on the wrong
  hypothesis). A new `IsListElement` (parent is a `BlockSyntax` or `SwitchSectionSyntax`) gates the three operators that
  replace one statement with several or none (IntroduceTemporary/InlineTemporary, DropNullCheck, DropFieldWrite). They
  offer no site on an embedded statement, the class's existing "does not offer that site" posture. One predicate, no
  redesign, so the Size guard does not trip. The DropFieldWrite/DropNullCheck part is in scope because it is the same
  embedded-statement edit and criterion 3 needs both pairs to complete.
- Decision: refuse the site rather than wrap the embedded statement in a new block. Wrapping would add a third
  trivia-normalisation path for little gain, since real code has plenty of list-element sites.
- Criterion 3, on Linux: before the fix, both `corpus.ps1 -SeedMechanical <pair> -Count 300 -Seed 1` runs crashed
  with the message above (seeder exit 134). After: Tomas seed 1 applied=97 dropped=1, seed 2 applied=97 dropped=0 (it
  has fewer seedable files than 300); gitextensions-8522 seed 1 applied=300 dropped=11, seed 2 applied=300 dropped=13.
  The Tomas modern side here is the `-PrepareAgent` copy, not agent-migrated; the seeder only reads `.cs` syntax, so
  this doesn't matter. The script's best-effort whole-copy build then warned "does not build" for both, but the
  unseeded copy fails the same way on this box (MSB3644, no .NET Framework 4.7.2 reference assemblies since `-Prepare`
  was not run), so that is the environment, not the seeds.
- No `tests/` project covers `tools/corpus/seeder` directly (only via `PairGen`, which renders every branch with
  braces, so its site counts are unchanged). `Equiv.Frontend.CSharp.Tests` `PairGen` tests pass. The integration
  `PairGen`/differential tests need `.z3-feed`, which this box lacks; CI runs them.
