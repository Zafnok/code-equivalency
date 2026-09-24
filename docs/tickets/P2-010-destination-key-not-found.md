# P2-010 `IrLowerer.Destination` throws `KeyNotFoundException` on Git Extensions
Status: in-progress
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
- Cause (confirmed in unit tests, not yet on the corpus): neither candidate shape in the Goal
  throws. The shape that does is a `try` whose `finally` never completes, because it always throws
  (`try { ... } finally { throw new X(); }`) or loops forever. Roslyn then marks the block after the
  `try` unreachable, so `LowerBlocks` never gives it an IR block. But the `try`'s fall-through (and
  any `goto` out of it) is still a Regular branch naming that block with `FinallyRegions = [finally]`.
  So `Destination` indexed `blockIds` with it. In the smallest shape that block is ordinal 3, which
  matches the census's "key '3'". Finally inside finally, `goto case` into a folded chain, `goto
  default`, a switch inside a `finally`, and `lock`/`using`/`foreach`/`while` inside a `finally` all
  lowered before the fix.
- Decision: when a branch's destination was not lowered, `Destination` continues at one shared
  `NeverReached` block instead of indexing `blockIds`. The `finally` copy in front of it never
  reaches its exit, so no edge jumps to that block and `SsaBuilder.Build` drops it without reading
  its terminator, so it gets none. An opaque exit there would be code no test can observe, and its
  mutants would survive. The
  alternative was to lower unreachable blocks too, but those can chain into more unreachable code,
  and that restructures the block maps, which the Size guard leaves to P1-003. `IrUnreachable` was
  not used: it means "assume false", and its doc says the frontend never produces it.
- Criterion 3 not run: this session ran in a Linux container. The loader is Windows-only (ADR 0004,
  `equiv-corpus-run`: "Windows only"; the Linux loader is M3-028/M3-029), and `gitextensions-8522`'s
  legacy side is a `net461` legacy csproj that needs VS Build Tools' MSBuild. The census, the failing
  procedure's identity and the refreshed `docs/runs/2026-09-23-census-gitextensions-8522/SUMMARY.md`
  still need a Windows box. Since P2-011, a pre-fix build names the failing procedure in the SARIF
  instead of aborting, so the identity can come from one census on the base commit.
