# timespan-integer-overloads

Identical source that binds to another `TimeSpan` factory overload on the modern side (ADR 0020 as
clarified for ticket P2-142, ADR 0042). .NET 9 added integer overloads of `FromDays`, `FromHours`,
`FromMinutes`, `FromSeconds` and `FromMilliseconds`, so a call that passes an integer binds the
`double` overload on .NET Framework 4.8 and the integer overload on .NET 10. The two return the same
value while the result is in the range of `TimeSpan`, and outside it the first throws
`OverflowException` and the second `ArgumentOutOfRangeException`. The catalogue's five
`bcl.timespan-from-*-integer` entries each state the range on which the two agree, and rewrite a
legacy call only when its argument is known to be inside it:

- a compile-time constant inside the range (`FromDays(7)`, `FromHours(CacheHours)`,
  `FromMinutes(5)`, `FromSeconds(30)`, `FromMilliseconds(-1)`);
- an argument that is not constant, of a type all of whose values are inside it: every `int` of
  seconds, minutes or milliseconds is in the range of `TimeSpan` (`Wait`).

Any other call is left as it is, so it stays a rebound call (ADR 0042) and its pair Unknown, with
the two callees in `properties.reboundCalls`: a constant out of range (`TooLong`), an `int` of hours
(`Hours`), which can be out of range, and a `long` of milliseconds (`Delay`), which can be out of
range or lose bits on its way through `double`.

`Backoff` passes 1 minute on the legacy side and 2 on the modern side: the entry still applies to the
legacy call, and the two calls have different arguments, so the pair stays Divergent.

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Timeouts.Week()` | Equivalent | `bcl.timespan-from-days-integer` |
| `Timeouts.CacheLifetime()` | Equivalent | `bcl.timespan-from-hours-integer` |
| `Timeouts.Retry()` | Equivalent | `bcl.timespan-from-minutes-integer` |
| `Timeouts.Poll()` | Equivalent | `bcl.timespan-from-seconds-integer` |
| `Timeouts.Never()` | Equivalent | `bcl.timespan-from-milliseconds-integer` |
| `Timeouts.Wait(int)` | Equivalent | `bcl.timespan-from-seconds-integer` |
| `Timeouts.Backoff()` | Divergent | `bcl.timespan-from-minutes-integer` |
| `Timeouts.TooLong()` | Unknown (`opaque`, a rebound call) | none |
| `Timeouts.Hours(int)` | Unknown (`opaque`, a rebound call) | none |
| `Timeouts.Delay(long)` | Unknown (`opaque`, a rebound call) | none |

Exit code: 1 (a new Divergent result).
