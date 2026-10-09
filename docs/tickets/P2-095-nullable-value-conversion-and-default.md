# P2-095 A conversion to `Nullable<T>` and `default(T?)` no longer keep a pair opaque
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-049

## Goal
P2-049's sample `cleanup-modern-syntax` rewrites a null test into a null-conditional access:

```csharp
// legacy
return x == null ? (int?)null : x.Length;
// modern
return x?.Length;
```

Today `Tidy.LengthOf(string)` is Unknown(opaque). The legacy side has one opaque node, reason
`Conversion`: the implicit conversion of `x.Length` from `int` to `int?` (`(int?)null` is a constant).
The modern side has two: `DefaultValue`, the `default(int?)` the CFG gives `?.` when the receiver is
null, and the same `Conversion`. `IOPERATION-COVERAGE.md` says a nullable conversion and a struct
type's `default` stay opaque. The two sides also spell "no value" differently, as a constant and as
a `DefaultValue`, so the pair cannot be proved until both are the same IR value.

Lower the wrapping conversion `T` to `T?` and the empty `T?` (the constant `null` converted to `T?`,
and `default(T?)`), so that the pair is Equivalent.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `Conversion`, `DefaultValue`, `IsNull` and `Binary`;
P2-087 (lifted binary operators, which needs the same value); `IL-COVERAGE.md` rows
`NullableUnwrap` and `NullableRewrap`; `samples/cleanup-modern-syntax/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide with `equiv-decide` how a `Nullable<T>` value is represented, for an integral or `bool`
   `T`. If it needs an IR sort or node that does not exist, go through `equiv-adr`'s bar test
   first. P2-087 criterion 2 asks the same question: use its representation if it has landed, and
   say so in `## Notes` if it has not. Log the `Decision:` line.
2. The implicit and explicit conversion `T` to `T?` lowers to a value that has a value. `null`
   converted to `T?` and `default(T?)` lower to one value that has none. Snapshot test.
3. The `IsNull` row's null test of a `Nullable<T>` operand reads that representation.
4. `Tidy.LengthOf(string)` in `samples/cleanup-modern-syntax` is Equivalent. The sample's snapshot
   and README row are updated. The `IOPERATION-COVERAGE.md` rows are updated.
5. A variant whose modern side is `x?.Length ?? 0` with return type `int?` is Divergent on a null
   string (integration test).

## Files
`src/Equiv.Frontend.CSharp/**` (the lowering of `Conversion` and `DefaultValue`), `src/Equiv.Core`
and `src/Equiv.Verify.Z3` only if criterion 1 adds to the IR (then follow `equiv-extend-ir`), their
test projects, `docs/tickets/IOPERATION-COVERAGE.md`,
`samples/cleanup-modern-syntax/expected.sarif.json` and `README.md`.

## Tests
- Lowering unit tests and a snapshot test for criteria 2 and 3.
- `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp` covers the new forms.
- `SamplesEndToEndTests` row for `cleanup-modern-syntax`; an integration test for criterion 5.

## Size guard
Lifted operators are P2-087's. `Nullable<T>` members (`Value`, `HasValue`, `GetValueOrDefault`)
beyond what criterion 3 needs: stop and file a ticket.

## Out of scope
The explicit conversion `T?` to `T`. A `Nullable<T>` of a floating-point, `decimal`, enum or
user-defined struct type. The IL lowering.

## Notes
- Found by P2-049 (`samples/cleanup-modern-syntax`, `Tidy.LengthOf`). Review group
  `opaque:Conversion+DefaultValue`.
- P2-123's split (2026-10-03): on `gitextensions-9860` this form is 13 of the 16 changed pairs that
  `Conversion` alone keeps opaque (2.2% of 725), and is in 34 changed pairs. 11 of the 13 have an
  `int` or `bool` `T`; the operand is a literal in 28 of the 59 nodes per side.
- P2-087 has not landed, so there was no representation to reuse; this ticket picks one, and P2-087 inherits it.
- Decision: representation of a `Nullable<T>` value -> what a boxed value already is: a value of the existing sort `System.Nullable`1` plus the Bool null shadow ("has no value"); `T` to `T?` is `mapread` of the `In` input `cast.<T>.System.Nullable_1`, `null` and `default(T?)` are the sort's null constant (element 0). No new IR sort or node, no backend change, so no ADR. Alternatives: the `IrTuple` sort `tuple(bool,bvN)` of a has-value flag and the value; a new nullable sort with solver axioms. Rule: 1 (the backend already consumes `cast` maps and null shadows as inputs, so a model that differs is a Divergent).
- The tuple alternative was built first and dropped. It proves criterion 4, but `tuple.new` and `tuple.item` are `IrPure`, and a divergence that depends on an `IrPure` result is Unknown(`abstraction`) (ADR 0026), so criterion 5 was Unknown, not Divergent. Making tuple functions exact in the replay is a backend change with its own soundness argument (a replay from a fragment's model reaches applications the formula never constrained); not this ticket's.
- Decision: which variables carry the shadow -> every `Nullable<T>` local, parameter and flow capture, of any `T`, as every reference does. Alternatives: flow captures only; only a `bool` or integral `T`. Rule: 4 (one condition added to `Shadowed`; the `IsNull` row already covered every `Nullable<T>`).
- Decision: `new T?(x)` and `new T?()` -> the conversion's value and the null constant, no call. Alternatives: leave the constructor a call. Rule: 3. Left a call, `new int?(x)` against `(int?)x` would be a call against a map read with no opaque on either side, which `--il-fallback` no longer retries; `samples/il-fallback`'s `Wrap` showed it.
- Decision: the conversion around a target-typed conditional (`b ? a : null`) -> its operand, as P2-099's target-typed `new()` is. Alternatives: leave it opaque. Rule: 4. Without it the commonest spelling of "a value or null" still ends in an opaque `Conversion`.
- Deviation: `samples/il-fallback` is outside the Files list and changes. Its `Wrap(int)` was `int?`, which now lowers on both sides and is proved without the fallback, so the sample no longer showed a modern-only opaque. It is now `long? Wrap(int)`: the modern side's `int` to `long?` also converts the value and stays opaque. `Add` is unchanged in source; its message loses `Conversion`.
- Precision limits, all the ones a boxing `cast` already has: nothing says `cast.<T>.System.Nullable_1` is one-to-one or never the null constant, and a `Nullable<T>` read back from a field, an array or a call asks `null.System.Nullable_1`. So a wrapped value stored in a field and tested there can be "null" in a model. No lowered operation reads the value out yet (`GetValueOrDefault` is a closed call), so a difference that needs the value is Unknown(`abstraction`), not Divergent.
- `b ? a : default` is typed `int` by C# (`default` is `0`), then converted: it is `(int?)(b ? a : 0)`, never null. The first draft of a test assumed otherwise and the solver found the difference.
- Out of scope and still opaque, with the same representation ready for them: `short` to `int?` (converts the value too), `T?` to `T?` of another `T`, and a floating-point, `decimal`, enum or struct `T`.
