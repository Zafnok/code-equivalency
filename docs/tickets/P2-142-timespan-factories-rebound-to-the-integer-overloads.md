# P2-142 `TimeSpan.FromHours(2)` binds to an integer overload on .NET 9, and the two overloads agree while the result is in range
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
.NET 9 added integer overloads of the `TimeSpan` factories. Source that passes an integer,
`TimeSpan.FromHours(2)` or `TimeSpan.FromSeconds(count)`, binds to `FromHours(double)` before and to
`FromHours(int)` after, so the call is a rebound call (ADR 0042) and the pair is Unknown. P2-137
counted them on `jellyfin-13023` (main of 2026-10-07, 162 changed pairs):

| Legacy callee | Modern callee | Results that name it |
|---|---|---|
| `System.TimeSpan::FromHours(double)` | `System.TimeSpan::FromHours(int)` | 16 |
| `System.TimeSpan::FromMilliseconds(double)` | `System.TimeSpan::FromMilliseconds(long,long)` | 15 |
| `System.TimeSpan::FromMinutes(double)` | `System.TimeSpan::FromMinutes(long)` | 10 |
| `System.TimeSpan::FromSeconds(double)` | `System.TimeSpan::FromSeconds(long)` | 10 |
| `System.TimeSpan::FromDays(double)` | `System.TimeSpan::FromDays(int)` | 1 |

They are in 49 changed pairs and alone keep 12 opaque (7.4% of the changed pairs, over ADR 0028's
5% line); they are 49 of the 69 changed pairs that hold `rebound-call` there. P2-137 gave them no
catalogue entry, because the two overloads differ outside the range of `TimeSpan`: the `double`
overload throws `OverflowException` and the integer overload `ArgumentOutOfRangeException`, and
ADR 0020 has no entry with a precondition. Inside the range both return the same value. Of the 96
calls of these factories in the legacy checkout (a text search), 52 pass an integer literal, always in
range.

## Spec references
ADR 0020 (soundness condition; "an equivalence that needs any further precondition is not an
entry"); ADR 0042; ADR 0014; VERIFICATION-MODEL section 3;
`docs/tickets/done/P2-137-rebound-call-forms-counted-and-catalogued.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, over `jellyfin-13023` (`--lower-only`), split the rebound calls of the five
   pairs above by what the integer argument is: a literal or constant in range, a constant out of
   range, a value that is not constant. Counts in `## Notes`.
2. Decide, through `equiv-adr`'s bar test, how a pair of callees that agree on a stated range of
   one argument is equated: an entry form whose adapter states the range and that applies only to a
   constant argument inside it, or both callees modelled as one pure function of the integer with
   the out-of-range exception type kept apart. Record the decision where the bar test says.
3. With that, a call of each of the five pairs whose argument is a constant in range is not a
   rebound call, and a sample pair with one method per pair is Equivalent.
4. A call whose argument is out of range, or not constant unless criterion 2's decision covers it
   soundly, stays Unknown or Divergent, with a sample method for each.
5. On a re-run of `jellyfin-13023`, the changed pairs that hold `rebound-call` fall by at least 5%
   of its changed pairs, or `## Notes` records why not.

## Files
`src/Equiv.Core/ApiEquivalences/`, `src/Equiv.Frontend.CSharp/Lowering/`, their tests, `samples/`,
the ADR criterion 2 names.

## Tests
A unit test per entry or rule, and the sample of criteria 3 and 4.

## Size guard
Members other than the five `TimeSpan` factories are another ticket.

## Out of scope
`TimeSpan.FromMilliseconds(long,long)` called with a second argument. The .NET Framework
`double` overloads' rounding to a millisecond, which only a fractional argument shows.

## Notes
- Found by P2-137, criterion 1.
