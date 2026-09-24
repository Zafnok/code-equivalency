# P2-010 `IrLowerer.Destination` throws `KeyNotFoundException` on Git Extensions
Status: done (PR #155)
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
3. ~~The census of `gitextensions-8522` completes, and its SUMMARY.md is refreshed with the result.~~
   Moved to M3-031 (criterion 7). See the `Deviation:` line in Notes.

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
- Deviation: criterion 3 is not met by this PR and moved to M3-031, as its criterion 7. **Follow-up
  required.** This session ran in a Linux container. The loader is Windows-only (ADR 0004;
  `equiv-corpus-run` says "Windows only"; the Linux loader is M3-028/M3-029), and
  `gitextensions-8522`'s legacy side is a `net461` legacy csproj that needs VS Build Tools' MSBuild.
  M3-031 already reruns this exact census on a Windows box, writes a fresh SUMMARY.md and depends on
  P2-010, so it is the rerun this criterion asked for. The failing procedure's identity (Goal) was
  not recorded. The cause is confirmed by the unit tests instead, and after this fix the census has
  nothing left to name.
- Second cause, found by running the census on Windows on 2026-09-24 with the fix above: with
  `KeyNotFoundException` gone, lowering stopped at
  `ICSharpCode.TextEditor.TextAreaClipboardHandler::Paste(object,System.EventArgs)` with a
  `NullReferenceException` in `Destination`. That was already on `main`; the first crash had hidden it.
  The shape is a conditional rethrow in a `catch` (`catch (X) { if (c) throw; }`). Roslyn makes it one
  block whose condition jumps past the rethrow and whose fall-through is the rethrow itself: Rethrow
  semantics, `Destination == null`. `Terminate`'s conditional case passed that fall-through to
  `Destination`. An unconditional `throw;` was already opaque ("rethrow"). Tests:
  `IrLowererTests.AConditionalRethrowInsideACatchLowers` and
  `IrLowererSnapshotTests.ConditionalRethrow`, which failed with the census's `NullReferenceException`
  before the fix.
- Decision: a conditional block whose fall-through is not Regular gets a fresh block for the
  fall-through, lowered as the same `rethrow` opaque exit as an unconditional `throw;`. The conditional
  case moved into `Branch` to keep `Terminate` under MA0051's 60 lines.
- Criterion 3, partly checked: after both fixes the `gitextensions-8522` census (`--lower-only`, Windows,
  Release) exits 0, with 0 tool-execution notifications and 0 unverified procedures: 13541 matched
  pairs, 315 changed, 24.4% of those without opaque. SUMMARY.md is still M3-031's to write.
- Deviation: developed on the harness-assigned branch (`claude/admiring-darwin-ra6ybn`) rather than a
  new `P2-010-...` branch.
