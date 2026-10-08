# P2-143 A `params` call that .NET 9 binds to the `params ReadOnlySpan<T>` overload is the same call as the `params T[]` one
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
C# 13 and .NET 9 added `params ReadOnlySpan<T>` overloads next to the `params T[]` ones. Source
that passes the elements, `string.Format(culture, format, a, b)`, binds to the array overload
before and to the span overload after, so the call is a rebound call (ADR 0042). P2-137 counted
them on `jellyfin-13023` (main of 2026-10-07, 162 changed pairs):

| Legacy callee | Modern callee | Results that name it |
|---|---|---|
| `System.String::Format(System.IFormatProvider,string,object[])` | `System.String::Format(System.IFormatProvider,string,System.ReadOnlySpan<object>)` | 16 |
| `System.String::Join(char,string[])` | `System.String::Join(char,System.ReadOnlySpan<string>)` | 2 |
| `System.Text.StringBuilder::AppendFormat(System.IFormatProvider,string,object[])` | `System.Text.StringBuilder::AppendFormat(System.IFormatProvider,string,System.ReadOnlySpan<object>)` | 1 |
| `System.IO.Path::Combine(string[])` | `System.IO.Path::Combine(System.ReadOnlySpan<string>)` | 1 |

The two overloads of each are one call on every list of elements: the documented behaviour is the
same, and the array overload's `ArgumentNullException` for a null array cannot happen when the
compiler builds the array from elements. P2-137 could not give them a catalogue entry. An adapter
(ADR 0020) lists argument positions and constants, so it cannot say "the rest of the elements as a
span", and on the modern side the span the compiler builds is a collection expression whose target
is a span, which stays opaque with reason `CollectionExpression` (P2-120 and P2-128 leave spans
out). All 20 changed pairs hold both reasons, so none is kept opaque by the rebound call alone; 3
hold exactly `CollectionExpression+rebound-call`.

## Spec references
ADR 0020; ADR 0042; VERIFICATION-MODEL section 3 (the collection expression paragraph and the
catalogue); `docs/tickets/IOPERATION-COVERAGE.md` row `CollectionExpression`;
`docs/tickets/done/P2-120-collection-expression-elements-and-other-targets.md`;
`docs/tickets/done/P2-137-rebound-call-forms-counted-and-catalogued.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The span the compiler builds for a `params ReadOnlySpan<T>` parameter from elements lowers to
   IR, with no opaque node. Decide the representation with `equiv-decide`; if it needs a sort or
   an IR node the IR does not have, go through `equiv-adr`'s bar test first.
2. An adapter form that passes the legacy call's `params` elements on as the modern call's span,
   so that a legacy call rewritten by an entry and the modern call are the same IR.
3. Entries for the four pairs above, each with its reason and Microsoft Learn page, a unit test
   per entry, and a sample pair with one method per entry that is Equivalent with the entry listed
   in `properties.equivalencesApplied`.
4. A call that passes an array, not elements, is not rewritten, and swapping two elements on the
   modern side stays Divergent.
5. On a re-run of `jellyfin-13023`, `## Notes` records the changed pairs that hold `rebound-call`
   and those that hold `CollectionExpression`, next to 69 and the count before.

## Files
`src/Equiv.Core/ApiEquivalences/`, `src/Equiv.Frontend.CSharp/Lowering/`, their tests, `samples/`,
`docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criteria 3 and 4.

## Size guard
A collection expression written in source whose target is a span is another ticket unless
criterion 1's lowering covers it with no further code.

## Out of scope
`params` of other collection types (`IEnumerable<T>`, `List<T>`). Spread elements (P2-128).

## Notes
- Found by P2-137, criterion 1.

### Criterion 1: the span (2026-10-08)
- Decision: the span the compiler builds for a `params ReadOnlySpan<T>` parameter -> a new `T[]` of
  the elements (the existing array creation: `new.<T[]>`, `length.<T[]>`, `array.<T[]>`), read
  through the `In` map `cast.<T[]>.<ReadOnlySpan>`. Alternatives: a new span sort with its own
  element map; an `IrPure` of the elements, one function per element count. Rule: 1 and 4. It is
  how P2-120 lowers an `IEnumerable<T>` target, a call already reads `array.*` as a heap pair so
  element order reaches the callee, and it needs no sort and no IR node the IR does not have, so
  `equiv-adr`'s bar test asks for no new ADR.
- Decision: an empty span -> an array of length 0, not the `System.Array::Empty<T>()` call an empty
  array target is. Alternatives: the `Array.Empty` call. Rule: 1. The compiler makes no call for
  an empty span, and a call would be a trace event neither side has.
- Decision: `ReadOnlySpan<T>` only; a `Span<T>` target stays opaque. Alternatives: both. Rule: 4.
  The ticket is about `params ReadOnlySpan<T>`, and a `Span<T>` is written through.
- The size guard's case is covered with no further code: a `ReadOnlySpan<T>` collection expression
  written in the source is the same operation with a conversion around it, and lowers the same way.
- Roslyn gives the compiler-built span as an implicit `ICollectionExpressionOperation` with no
  conversion around it, whose syntax is the invocation and whose argument has kind
  `ParamCollection`; the lowering only knew a collection expression under its conversion.

### Criterion 2: the adapter form
- Decision: `{"rest": n}`, `ApiArgument.Rest` -> every source argument from position n to the last,
  which must be exactly the elements of the legacy call's `params` array, as the span above.
  Alternatives: a `convertTo` on each element (cannot say how many); a second table of "same
  member, span overload". Rule: 4. Recorded as a dated clarification on ADR 0020 (the bar test's
  first row: the ADR's adapter applied to a case it did not spell out), in VERIFICATION-MODEL
  section 3 and in the catalogue's header.
- Decision: the legacy side names the span by its sort, `System.ReadOnlySpan`1`, not by a symbol ->
  the entry applies on a framework that does not declare the type. Alternatives: resolve the type
  in the legacy compilation and leave the call alone where it is missing. Rule: 4.
- The elements are evaluated where the legacy call evaluates them, each stored before the next, as
  the modern side builds its span, so the rewritten call and the modern call are the same IR text.
  One difference is left where the control flow graph evaluates an element ahead of the call
  (`a ?? b`): the legacy graph also captures the array's length, a constant nothing reads.

### Criteria 3 and 4: entries, tests, sample
- Entries: `bcl.string-format-provider-params-span`, `bcl.string-join-char-params-span`,
  `bcl.string-builder-append-format-provider-params-span`, `bcl.path-combine-params-span`.
- Tests: `ParamsSpanEntriesTests` (Core), `ParamsSpanLoweringTests` (frontend),
  `SamplesEndToEndTests.ParamsSpanOverloads_*` on `samples/params-span-overloads`.
- Decision: the sample pairs .NET 8 with .NET 10 -> SDK-style projects on both sides, as
  `version-bump`. Alternatives: .NET Framework 4.8 on the legacy side. Rule: 1. .NET Framework has
  no `String.Join(char, params string[])`, and with two to four arguments it binds the fixed
  overloads of `Format`, `AppendFormat` and `Combine`; the pairs P2-137 counted are a .NET 8 to
  .NET 9 upgrade.
- The four methods are Equivalent with `proofMethod: bounded`, not by congruence: such an entry does
  not pass its arguments through, so the fingerprint leaves its calls under their legacy name.

### Criterion 5: `jellyfin-13023` (2026-10-08)
- Two `equiv compare --lower-only` runs (a census takes no compare mode and no `--jobs`), default
  config, the checkouts `tools/corpus/pairs.csv` pins (legacy 5e8c0fe40c0e, modern ceb850c77052):
  one with `main` at `b68abf40`, one with this branch. Both exit 0, no project skipped, 14,532
  matched pairs, 14,370 congruent, 162 changed. Counts are from
  `run.properties.loweringCensus.changedReasonSets`, a reason "in" a pair when its set holds it.

| Changed pairs (of 162) | P2-137 (`main`, 2026-10-07) | Before (`main` at `b68abf40`) | After |
|---|---|---|---|
| hold `rebound-call` | 69 | 69 | 53 |
| hold `rebound-call` only | 12 | 12 | 12 |
| hold `CollectionExpression` | not recorded | 20 | 0 |
| hold both | 20 (the pairs that name a `params` span pair) | 20 | 0 |
| hold exactly those two | 3 | 3 | 0 |
| hold no opaque | not recorded | 23 | 26 |

- `rebound-call` falls by 16 changed pairs, 9.9% of the 162. The other 4 of the 20 pairs that named
  a `params` span pair still hold a rebound call of another callee pair (the `TimeSpan` factories
  of P2-142 among them).
- `CollectionExpression` leaves every changed pair. Bodies that hold it go from 38 legacy and 58
  modern to 37 and 37; those 37 are in congruent pairs, with the same source on both sides.
- The 3 pairs that held exactly the two reasons now hold no opaque, so the changed pairs without
  opaque go from 23 to 26. The other 17 hold further reasons.
- No verifying run was made: the criterion asks for the two counts, and a census gives them.
  Nothing under `docs/runs/` is added, so the README's scoreboard is not touched.
