# P2-095 A conversion to `Nullable<T>` and `default(T?)` no longer keep a pair opaque
Status: todo
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
