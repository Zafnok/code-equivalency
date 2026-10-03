# P2-094 Precision bug: `string.Format` with plain holes is Divergent from the interpolated string it becomes
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-049

## Goal
P2-049's sample `cleanup-modern-syntax` rewrites a `string.Format` call into an interpolated string:

```csharp
// legacy
return string.Format("{0}-{1}", a, b);   // a and b are strings
// modern
return $"{a}-{b}";
```

Today `Tidy.Join(string, string)` is Divergent (EQ002), and the pair is behaviour-preserving, so this
is a false positive. P2-086 lowers the interpolated string to the closed
`System.String::Concat(string,string)` chain of its parts. The explicit call stays a call to
`System.String::Format(string,object,object)`. The two call traces differ (ADR 0018), and each
call's result and `threw` flag are free, so the counterexample has one side throw.

P2-086's own argument applies here: a `string.Format` binding of an interpolated string and the
`Concat` chain compute the same value. Lower an explicit `string.Format` call whose format is a
constant string with only plain `{n}` holes, and whose arguments are the hole types P2-086 accepts,
to the same `Concat` chain.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `InterpolatedString` and `Invocation`;
`docs/tickets/done/P2-086-interpolated-string-owner.md` (the representation and its evaluation-order
limits); ADR 0041 (closed calls); ADR 0018; P2-102 (an integer hole before a hole that runs code).

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide with `equiv-decide`, and through `equiv-adr`'s bar test if the decision needs more than
   P2-086's representation, which `string.Format` calls lower: the overloads, a format with a hole
   used twice or out of order, escaped braces. Log the `Decision:` line.
2. A `string.Format` call the decision covers lowers to the IR of the interpolated string with the
   same text and holes. Snapshot test with both forms. Every other `string.Format` call stays the
   call it is today.
3. A `null` format argument, a format that is not a constant, a hole with an alignment or format
   clause, an `IFormatProvider` overload and a hole index past the arguments stay a call.
4. `Tidy.Join(string, string)` in `samples/cleanup-modern-syntax` is Equivalent. The sample's
   snapshot and README row are updated.
5. A variant whose modern side changes the separator stays Divergent (integration test).

## Files
`src/Equiv.Frontend.CSharp/**` (the lowering of `Invocation` and of `InterpolatedString`), its test
project, `docs/tickets/IOPERATION-COVERAGE.md`, `samples/cleanup-modern-syntax/expected.sarif.json`
and `README.md`.

## Tests
- Lowering unit tests for criteria 2 and 3, and a snapshot test with both forms.
- `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp` covers the lowered call.
- `SamplesEndToEndTests` row for `cleanup-modern-syntax`; an integration test for criterion 5.

## Size guard
A change to how the interpolated string itself lowers is P2-102's, not this ticket's: stop.

## Out of scope
`string.Concat` and `+` chains (already the same IR). `StringBuilder.AppendFormat`. Holes of types
P2-086 leaves opaque.

## Notes
- Found by P2-049 (`samples/cleanup-modern-syntax`, `Tidy.Join`). Review group
  `calls:System.String::Concat(string,string)|System.String::Format(string,object,object)`.
