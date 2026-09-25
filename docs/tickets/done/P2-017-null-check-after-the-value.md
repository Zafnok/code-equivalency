# P2-017 A dereference is null-checked where the CLR checks it: after the operands
Status: done (PR #169)
Effort: S
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-006

## Goal
`IrLowerer.Assign` calls `heap.Slice(assignment.Target, ...)` before it lowers the value, and
`HeapLowerer.Field`/`Element` emit the receiver or array null check right there. The CLR throws
`NullReferenceException` at the `stfld`/`stelem`, after the right-hand side has run. So
`a[0] = F();` with `a == null` calls `F` in C# and not in the IR. That changes the observable
call trace and can produce a false Equivalent, e.g. `a[0] = F();` against
`if (a == null) throw new NullReferenceException(); a[0] = F();`. `Assign` already moves the
bounds check after the value for this reason. The null check stayed before it. An element read
has the same gap (`a[F()]` evaluates the index before `ldelem` null-checks `a`), and so does a
call: `IrLowerer.Operands` null-checks the receiver before the arguments, but `callvirt` checks
it at the call, so `o.M(F())`, `o.P = F()` and `o[F()]` all call `F` first. Found in P1-006
(its Notes).

## Spec references
VERIFICATION-MODEL.md section 2 (the Null paragraph) and section 7 (the lowering oracle);
P1-006's Notes; `HeapLowerer`, `IrLowerer.Assign`, `IrLowerer.Operands`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A compiled C# probe confirms the runtime order (recorded in Notes).
2. The null check on a field's receiver, an element's array or a call's receiver is emitted at
   the load, store or call, after every operand the CLR evaluates first (index, arguments, a
   stored value) and before the bounds check. Unit tests show that with a null target
   `o.f = checked(n + 1)`, `o[0] = checked(n + 1)`, `o[checked(n + 1)]` (array and indexer),
   `o.G(checked(n + 1))` and `o.P = checked(n + 1)` throw `OverflowException` when the operand
   overflows and `NullReferenceException` otherwise, and `o.P += checked(n + 1)` still throws
   `NullReferenceException` first (the getter runs before the value).
3. `LoweringOracleGen`'s inputs can bind `v` to `null`, and the oracle compares the outcome and
   final heap for those inputs. Adding that input before the fix fails the oracle.
4. VERIFICATION-MODEL's Null paragraph says where the check sits.

## Files
`src/Equiv.Frontend.CSharp/Lowering/HeapLowerer.cs`, `IrLowerer.cs`;
`tests/Equiv.TestSupport/LoweringOracleGen.cs`, `OracleInput.cs`, `ArrayBinding.cs`;
`tests/Equiv.Frontend.CSharp.Tests/Lowering/LoweringOracleTests.cs`, `IrLowererTests.cs`;
`docs/VERIFICATION-MODEL.md`.

## Tests
`IrLowererTests.ANullTargetIsCheckedAfterItsOperands`,
`IrLowererTests.ACompoundAssignmentToANullReceiversPropertyThrowsBeforeTheValue`, the extended
lowering oracle, re-approved snapshots whose only diff is the null check moving.

## Size guard
Moves one check. Compound assignment to fields and elements (still opaque) and instance
receivers in the oracle stay as they are.

## Out of scope
`a.Length` (`ldlen` has no operand after the array, so its check is already in place).

## Notes
- Criterion 1, probe (`dotnet run order.cs`, .NET 10, a null `int[] a`, `O o` and `string s`,
  `F` counting its calls): `a[0] = F()`, `o.X = F()`, `x = a[F()]`, `a[F()] = 0`, `o.M(F())`,
  `o.P = F()`, `o[F()]`, `o[0] = F()` and `s.CompareTo(G())` all throw
  `NullReferenceException` with one call to `F`/`G`. `a[0] += F()`, `o.X += F()` and `a[0]++`
  throw with no call: the load comes before the value. `e[5] = F()` on an empty array throws
  `IndexOutOfRangeException` after `F`, as `Assign` already modelled.
- Deviation: the ticket was first written for fields and elements only. The probe showed
  calls, setters and indexers have the same order, and `IrLowerer.Operands` had the same
  early check, so the ticket now covers them too. Leaving them would keep the same false
  Equivalent for `o.M(F())`.
- Criterion 3, checked before the fix: the oracle failed on its first sample,
  `OracleInput { A = 33, B = 32, ..., V = Null }`: C# `throw System.OverflowException F=33 u=33,32 v=null`,
  IR `throw System.NullReferenceException F=33 u=33,32 v=null`. The written value
  (`checked(...)` in `FieldValue`) overflows before the store's null check.
- Decision: `OracleInput.Aliased` became `ArrayBinding V` (`Distinct`, `Aliased`, `Null`), since a
  null `v` and an aliased `v` exclude each other. A null `v` is `sort "int[]"` element 3, the one
  element `null.int__` answers true for.
- Decision: `HeapLowerer.Access` carries the operand it dereferences (`Dereferenced`), and
  `ReadSlice`/`WriteSlice` null-check it first, then check bounds. Calls null-check the receiver in
  a new `IrLowerer.Dispatch`, used by `Invoke` and `Accessor`, right before the `IrCall`.
  `Operands` no longer checks. A compound property assignment's setter re-checks the receiver
  its getter already checked: a branch that is never taken (snapshot
  `CompoundAssignmentToAProperty`), kept instead of threading an "already checked" flag through
  `Accessor`.
- Deviation: branched from `P1-006-array-maps-keyed-by-value` (PR #166, open), not `main`. This
  ticket depends on P1-006, which rewrites the same `HeapLowerer` array path and oracle harness.
  The PR targets that branch and is retargeted to `main` once #166 merges.
