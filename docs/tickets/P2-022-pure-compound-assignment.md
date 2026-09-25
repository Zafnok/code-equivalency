# P2-022 Compound assignment and `++`/`--` on `float`, `double`, `decimal` and user-defined operators apply the pure functions
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-002

## Goal
M4-002 lowers binary, unary and conversion operators on `float`, `double` and `decimal`, and every
user-defined operator, to `IrPure` applications of `PureCatalogue` functions. It left compound
assignment and `++`/`--` on those types opaque (its Notes, "Decision: criterion 3 names binary,
unary and comparison operators..."), because `IrLowerer.Compound`, `Step` and `Update` handle only
bitvector operands: `Compound` needs `OperatorMethod is null` and a bitvector right operand, `Step`
needs `TypeMapper.Promote`, and `Update` needs a bitvector target. So `x op= y` is opaque where
`x = x op y` is not. `samples/business-layer` `OrderService.Subtotal` is the example in the repo:
`subtotal += line.UnitPrice * line.Quantity;` on `decimal`. Minimal repro:

```csharp
static decimal Sum(List<decimal> prices) { decimal s = 0m; foreach (var p in prices) s += p; return s; }
static decimal Next(decimal m) => ++m;
static double Halve(double d) { d /= 2; return d; }
static Money Add(Money a, Money b) { a += b; return a; }   // Money declares a static operator +
```

After this ticket each of these reads the target once, evaluates the right operand, applies the
same catalogued function, with the same exception flags, as the matching binary operator, and
writes the result back. No new function, IR instruction or encoding is needed.

What this buys is proofs across a common syntactic rewrite. `x = x - y` becoming `x -= y` is what
the IDE0054 analyzer ("use compound assignment") does during a migration, and today such a pair is
Unknown on `decimal` or `double`. `Subtotal` alone cannot show that: it is unchanged, so it is
already Equivalent by congruence. So the ticket adds a changed method to `business-layer` in which
legacy writes the expanded form and modern the compound one. It moves from Unknown to Equivalent,
which is the ratchet the README asks every precision ticket for. No census has counted how many
real changed pairs this unlocks.

## Spec references
ADR 0025; ticket M4-002 (its criterion 3 and its Notes' `Decision:` lines on checked conversions,
user-defined operators and compound assignment); M2-004 criterion 9 (compound assignment:
read, operate, write); M3-010 criterion 2 (property targets: receiver and index arguments once);
`docs/tickets/IOPERATION-COVERAGE.md` rows `CompoundAssignment`, `Increment`, `Decrement`; the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `x op= y` with `op` one of `+ - * / %` and a `float`, `double` or `decimal` target, not lifted,
   lowers in this order: read the target, evaluate `y` (as Roslyn converted it, so `m += i` on an
   `int i` applies `conv.i32.dec` first), apply the `PureCatalogue.Binary` entry for the
   operator and the target's type, branch on each of its flags, and write the result back. The
   expression's value is the value written. Targets are those `Place` accepts today: a local, a
   parameter, and a property with a getter and a non-init setter, whose receiver and index
   arguments are evaluated once. The catalogue is consulted before `OperatorMethod`, as
   `IrLowerer.Binary` does. Snapshot `IrLowererSnapshotTests.PureCompoundAssignment`.
2. The `IrPure` in `x op= y` has the same function, flag types and `RuntimeSensitive` as the one
   in `x = x op y`, in both checked and unchecked contexts and on an x87 legacy side. Test
   `CompoundAssignmentMatchesItsBinaryOperator`.
3. A flag branches to its throw before the write. A `decimal` `+=` that overflows, or a `/=` by
   zero, leaves a local target unchanged, and on a property target it calls the getter but not the
   setter. Test `ACompoundOverflowLeavesTheTargetUnwritten`.
4. `x++`, `++x`, `x--` and `--x` on a `float`, `double` or `decimal` target lower as
   `x += 1` and `x -= 1`, with `1` being the same `IrConst` the literal `1f`, `1d` or `1m` lowers to.
   A postfix form yields the value read and a prefix form the value written. `decimal` `++` is
   `dec.add` with its `System.OverflowException` flag, whether or not Roslyn reports
   `System.Decimal.op_Increment` as the operation's `OperatorMethod`. Test
   `IncrementIsAddingTheLiteralOne`.
5. A compound assignment whose `OperatorMethod` is a static user-defined binary operator, and whose
   `InConversion` and `OutConversion` are identities, lowers as in criterion 1 with M4-002's
   `op:<identity>` function. That means one `System.Exception` flag routed with `known: false`,
   runtime-sensitive when the identity is runtime-changed, and used only when the target and
   operand types are exactly the method's parameter types and its return type is the target's
   type. `++` and `--` with a static user-defined `op_Increment` or `op_Decrement` lower the same
   way with one argument. Test `UserDefinedCompoundAndIncrementAreTheirOpFunctions`.
6. These stay opaque with their current reason (`CompoundAssignment`, `Increment`, `Decrement`):
   - a lifted operator (`decimal? m; m += 1`);
   - a compound assignment on these paths whose `InConversion` or `OutConversion` is not an
     identity;
   - a C# 14 instance compound-assignment or increment operator (a `void` instance
     `operator +=` or `operator ++`), which mutates its receiver and is therefore not pure.

   A target `Place` rejects stays opaque with the target's kind, as today. Test
   `LiftedConvertedOrInstanceCompoundOperatorsStayOpaque`.
7. End to end, `x op= y` and `x = x op y` are Equivalent for `decimal` and `double`, for `m++`
   against `m = m + 1m`, and for a user-defined `+=` against `a = a + b`. The sources differ, so
   these pairs are not congruent and the solver decides them. Test
   `CompoundAssignmentIsEquivalentToItsExpandedForm`.
8. The lowering oracle's `decimal` case also generates `m op= <decimal expression>` for each of
   `+ - * / %`, and `m++`, `++m`, `m--` and `--m`. `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`
   stays green, and the test asserts that the lowered IR reaches a `dec.add` that comes from a
   compound assignment or an increment.
9. `business-layer` gains `OrderService.Discounted(decimal total, decimal rate)`, written as
   `total = total - total * rate; return total;` on the legacy side and
   `total -= total * rate; return total;` on the modern side. It is Unknown before this ticket
   (the modern side's `CompoundAssignment` opaque) and Equivalent after it, decided by the solver
   and not by congruence (test `BusinessLayerDiscountedIsEquivalent`). The README gets a row for
   it, with Today Equivalent, Target Equivalent and Unlocked by P2-022.
10. On `business-layer`, `OrderService.Subtotal` lowers with no `IrOpaque` on either side (test
    `BusinessLayerSubtotalHasNoOpaque`). `LoweringCensusTests.BusinessLayerCensusSnapshot` no
    longer has a `CompoundAssignment` entry. The new method raises the procedure, matched-pair and
    changed-pair counts, and the PR explains every count that moves. The README's `Subtotal` row
    names P2-022 as the ticket that lowers its `decimal` `+=`.
11. `IOPERATION-COVERAGE.md` rows `CompoundAssignment`, `Increment` and `Decrement` describe the
    new lowering and name the tests above, with P2-022 in the Ticket column.

## Files
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`;
`tests/Equiv.Frontend.CSharp.Tests/Lowering/PureLoweringTests.cs`;
`tests/Equiv.Frontend.CSharp.Tests/Lowering/IrLowererSnapshotTests.cs` and
`IrLowererSnapshotTests.PureCompoundAssignment.verified.txt`;
`tests/Equiv.Frontend.CSharp.Tests/Lowering/LoweringOracleTests.cs`;
`tests/Equiv.TestSupport/LoweringOracleGen.cs`;
`tests/Equiv.Tests.Integration/PureOperatorTests.cs`;
`tests/Equiv.Tests.Integration/LoweringCensusTests.BusinessLayerCensusSnapshot.verified.txt`;
`samples/business-layer/legacy/OrderService.cs`; `samples/business-layer/modern/OrderService.cs`;
`samples/business-layer/README.md`; `docs/tickets/IOPERATION-COVERAGE.md`. Only if they pin a
`business-layer` procedure or pair count that the new method moves:
`tests/Equiv.Tests.Integration/SamplesFixtureTests.cs`, `UnknownLocationSampleTests.cs` and
`WholeBodyReasonOwnerTests.cs`, or their snapshots.

## Tests
`PureLoweringTests.CompoundAssignmentMatchesItsBinaryOperator` (theory: `float`, `double`,
`decimal` × `+ - * / %`, checked and unchecked, x87 legacy side),
`PureLoweringTests.ACompoundOverflowLeavesTheTargetUnwritten` (local and property target, the
interpreter's pure oracle raising the flag), `PureLoweringTests.IncrementIsAddingTheLiteralOne`,
`PureLoweringTests.UserDefinedCompoundAndIncrementAreTheirOpFunctions`,
`PureLoweringTests.LiftedConvertedOrInstanceCompoundOperatorsStayOpaque`,
`IrLowererSnapshotTests.PureCompoundAssignment` (a `decimal` `+=` to a local, a `decimal` `/=` to a
property, a postfix `double` `++`, a user-defined `+=`),
`PureOperatorTests.CompoundAssignmentIsEquivalentToItsExpandedForm`,
`PureOperatorTests.BusinessLayerDiscountedIsEquivalent`,
`PureOperatorTests.BusinessLayerSubtotalHasNoOpaque`,
`LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp` (extended), and the updated
`LoweringCensusTests.BusinessLayerCensusSnapshot`.

## Size guard
Eighteen files, or any change outside `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` in `src/`,
means the ticket has been misread. The catalogue already has every function needed. A new
`PureCatalogue` entry, IR instruction or Z3 encoding is a scope question for `equiv-adr`.

## Out of scope
`string +=` (a `String.Concat` call) and delegate or event `+=`/`-=` (P2-005). `??=`. Lifted and
nullable operands. Field targets `Place` does not accept (P2-007). C# 14 instance compound
operators as calls: they stay opaque until a census finds them. Any change to `Equiv.Core` or
`Equiv.Verify.Z3`.

## Notes
