# bcl-rebound-overloads

Identical source text that binds to another base class library overload on the modern side, one
form the API-equivalence catalogue equates and one it does not (ADR 0020, ADR 0042, ticket P2-137;
the callee pairs were counted over `gitextensions-8522` and `jellyfin-13023`):

- `s.TrimEnd()` binds `String.TrimEnd(params char[])` with an empty array on .NET Framework 4.8 and
  `String.TrimEnd()` on .NET 10. Both remove the trailing white-space characters and neither
  throws, so the catalogue rewrites the legacy call (`bcl.string-trim-end-no-chars`) and the pair is
  Equivalent. The legacy member has a second entry, for one element; a call takes the first entry
  whose adapter addresses its arguments.
- `TimeSpan.FromHours(hours)` with an `int` binds `TimeSpan.FromHours(double)` on .NET Framework
  4.8 and `TimeSpan.FromHours(int)` on .NET 10. They return the same value while the result is in
  range, and outside it the first throws `OverflowException` and the second
  `ArgumentOutOfRangeException`. The two agree only on some arguments, and the entry for them
  (ticket P2-142, sample `timespan-integer-overloads`) applies only to an argument known to be in
  range, which an `int` of hours is not: the call is a rebound call (ADR 0042) and the pair is
  Unknown, with the two callees in `properties.reboundCalls`.

`TrimTailOther` trims white space on the legacy side and `'.'` on the modern side: the entry still
applies to the legacy call, and `TrimEnd()` and `TrimEnd(char)` are two different calls, so the pair
stays Divergent.

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Calls.TrimTail(string)` | Equivalent | `bcl.string-trim-end-no-chars` |
| `Calls.TrimTailOther(string)` | Divergent | `bcl.string-trim-end-no-chars` |
| `Calls.Hours(int)` | Unknown (`opaque`, a rebound call) | none |

Exit code: 1 (a new Divergent result).
