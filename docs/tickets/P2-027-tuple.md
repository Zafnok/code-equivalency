# P2-027 Tuple literals and `.Item1`/named tuple fields have no lowering
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `Tuple` in Git Extensions' opaque reasons (54 occurrences legacy
and modern), and no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static int SumPair((int X, int Y) p) => p.X + p.Y;
static (int, int) MakePair(int a, int b) => (a, b);
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `Tuple` row yet); `PropertyReference`'s row (an
auto-property backed by a field is the closest existing precedent for a tuple element read); the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, the IR representation for a value-tuple: most likely a fixed-arity
   record-like sort with one field per element (`.Item1`/`.Item2`/named tuple field access all
   read the same underlying element by position, since names are compile-time only). Add the
   `IOPERATION-COVERAGE.md` row either way (lowered or opaque).
2. Snapshot tests for a tuple literal, a tuple parameter's element read by position, and the same
   read through a named tuple field.
3. `==`/`!=` on tuples (element-wise) can stay opaque with reason `Tuple` if in scope creep;
   record that decision in Notes rather than expanding this ticket to cover it.

## Size guard
2- and 3-element tuples of primitive types only. A tuple containing a reference type, a nested
tuple, or arity above 3 can stay opaque; note that in `IOPERATION-COVERAGE.md`'s row rather than
handling it here.

## Out of scope
Tuple deconstruction (P2-025), `ValueTuple` used explicitly by type name instead of tuple syntax.

## Notes
- Decision: IR representation of a value tuple -> an uninterpreted `Sort` named `tuple(<element IR types>)` (e.g. `tuple(bv32,bool)`), a literal the pure function `tuple.new` of its elements, an element read (`.Item1` or a named field alike, by position) the pure function `tuple.item<n>`; `IrTuple` in `Equiv.Core.Ir` is the one definition of these names, as `IrParameterNames` is for inputs. Alternatives: a new IR record type plus instructions; a Z3 datatype sort (its model values are not sort elements, so `ModelDecoder` would need a tuple value). Rule: 4.
- Decision: exactness in Z3 -> ground axioms asserted at each application, no quantifiers: `item<i>(new(a..)) = a_i` at every `tuple.new`, and `t = new(item1(t), ..)` at every `tuple.item<n>` of `t` (so two tuples with equal elements are equal wherever either is read). Both are theorems of tuples, so asserting them unguarded removes no real run. Alternatives: quantified axioms; frontend constant folding of `new` then `item`. Rule: 1.
- Decision: a write to a supported tuple's element (`p.X = 1`, `p.X += 1`) -> opaque with reason `Tuple`, since the element is no longer a heap map. Alternatives: lower as `tuple.new` of the other elements and the new one. Rule: 4 (Size guard).
- Acceptance criterion 3: tuple `==`/`!=` stays opaque. Its `OperationKind` is `TupleBinaryOperator`, and the lowerer names an unsupported kind's reason after the kind, so the reason is `TupleBinaryOperator`, not `Tuple` (row added; `TupleLoweringTests.TupleEqualityStaysOpaque`).
- `tuple.new`/`tuple.item<n>` are exact, but the interpreter taints every `IrPure` result (ADR 0026), so a divergence that flows through a tuple function can be reported `Unknown(Abstraction)` rather than `Divergent`. The soundness tests only assert "never Equivalent". Untainting the tuple functions would be a follow-up, not this ticket.
- Before this ticket a tuple element read was the `field.System.ValueTuple`2.<name>` map, so `p.X` and `p.Item1` were different maps; that is still the case for tuples outside the size guard.
- `Equiv.Core.Tests` `EtaEstimatorTests.IsNonNegativeAboveTheItemThreshold` failed once locally and passed on rerun; unrelated to this change.
