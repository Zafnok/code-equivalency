# params-span-overloads

A version upgrade, .NET 8 to .NET 10, of source that passes the elements of a `params` parameter
(ADR 0020, ADR 0042, ticket P2-143). C# 13 and .NET 9 added `params ReadOnlySpan<T>` overloads next
to the `params T[]` ones, so the same source text binds to the array overload on .NET 8 and to the
span overload on .NET 10:

- `string.Format(provider, format, a, b, c, d)`
- `string.Join(separator, a, b)`
- `text.AppendFormat(provider, format, a, b, c, d)`
- `Path.Combine(a, b, c, d, e)`

The two overloads of each are one call on every list of elements, and the array overload's
`ArgumentNullException` for a null array cannot happen when the compiler builds the array. The
catalogue has an entry for each pair, whose adapter passes the legacy call's elements on as the
span (`{"rest": n}`). On the modern side the span the compiler builds is a new array of the elements
read through the `cast` map of the array to the span, so the rewritten legacy call and the modern
call are the same IR, and each pair is Equivalent with its entry applied.

`RowSwapped` passes the same two elements in the other order on the modern side: the entry still
applies to the legacy call, the spans hold the elements in another order, and the pair stays
Divergent. `RowOfArray` passes an array, not elements: both sides bind the `params string[]`
overload, no entry applies, and the pair is Equivalent by congruence.

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Texts.Describe(IFormatProvider, string, object, object, object, object)` | Equivalent | `bcl.string-format-provider-params-span` |
| `Texts.Row(char, string, string)` | Equivalent | `bcl.string-join-char-params-span` |
| `Texts.Append(StringBuilder, IFormatProvider, string, object, object, object, object)` | Equivalent | `bcl.string-builder-append-format-provider-params-span` |
| `Texts.Locate(string, string, string, string, string)` | Equivalent | `bcl.path-combine-params-span` |
| `Texts.RowSwapped(char, string, string)` | Divergent | `bcl.string-join-char-params-span` |
| `Texts.RowOfArray(char, string[])` | Equivalent (`proofMethod: congruence`) | none |

Exit code: 1 (a new Divergent result).
