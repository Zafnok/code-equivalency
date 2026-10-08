# P2-143 A `params` call that .NET 9 binds to the `params ReadOnlySpan<T>` overload is the same call as the `params T[]` one
Status: todo
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
