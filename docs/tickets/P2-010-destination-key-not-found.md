# P2-010 `IrLowerer.Destination` throws `KeyNotFoundException` on Git Extensions
Status: todo
Effort: S
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-024

## Goal
M3-022's census of `gitextensions-8522` aborts with exit 1. The stack is
`IrLowerer.Destination` → `Terminate` → `Fill` → `LowerBlocks` → `Lower`, and the exception is
"The given key '3' was not present in the dictionary". `Destination` indexes `blockIds` with the
branch's destination ordinal, but `blockIds` holds only reachable blocks that `SwitchChains` did not
absorb and that are not inside a `finally`, and inside `Copy` it is swapped for the copy's own map.
`Handler` got a `mainBlocks` fallback for the same class of bug (P1-003, PR #30). `Destination`
did not. Candidate shapes, to confirm first:

```csharp
static int A(int x) { try { return x; } finally { try { Log(); } finally { Flush(); } } }  // finally inside finally
static int B(int k) { switch (k) { case 1: goto case 2; case 2: return 2; default: return 0; } }  // branch into an absorbed chain block
```

Reproduce on the corpus first: run the `equiv-corpus-run` skill's `census` mode on
`gitextensions-8522` under a debugger, record the failing procedure's identity (identity only; no
source text) in Notes, then write a unit test in your own code with the same shape.

## Spec references
`IrLowerer.Destination`, `Handler`, `Copy`, `LowerBlocks`; `SwitchChains.IsAbsorbed`; P1-003's Goal.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test reproduces the `KeyNotFoundException` before the fix and lowers after it.
2. The lowered IR validates (`IrValidator`) and its snapshot is committed.
3. The census of `gitextensions-8522` completes, and its SUMMARY.md is refreshed with the result.

## Size guard
A fix of the lookup and its tests. Restructuring the swapped state is P1-003.

## Out of scope
Containing lowering exceptions in general (P2-011).

## Notes
