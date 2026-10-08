# P2-142 `TimeSpan.FromHours(2)` binds to an integer overload on .NET 9, and the two overloads agree while the result is in range
Status: done (PR #433)
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

### Criterion 1: the rebound calls by argument (2026-10-08)
- Run: `equiv compare --lower-only` of `jellyfin-13023`, default config, on `main` at `b68abf40`,
  before this ticket's change: 162 changed pairs, `rebound-call` in 69 and alone in 12, as P2-137
  found. A census holds no call sites, so the run also wrote, through a local patch that is not in
  this PR, one line per call the lowering made opaque as a rebound call of a `TimeSpan` factory:
  callee, whether its first argument without its conversions is a constant, the constant, its type.
- The legacy side holds 62 such calls and the modern side 60. A call is marked by its callee's
  identity and not by its site (ADR 0042), so the legacy 62 include 2 calls of `FromSeconds(double)`
  that pass a `double` literal, in bodies where the same member is rebound at another site.

| Legacy callee | Calls | Integer constant in range | Constant out of range | Not constant: `int` | Not constant: `long` | A `double` |
|---|---|---|---|---|---|---|
| `System.TimeSpan::FromDays(double)` | 1 | 1 | 0 | 0 | 0 | 0 |
| `System.TimeSpan::FromHours(double)` | 16 | 16 | 0 | 0 | 0 | 0 |
| `System.TimeSpan::FromMinutes(double)` | 14 | 14 | 0 | 0 | 0 | 0 |
| `System.TimeSpan::FromSeconds(double)` | 14 | 5 | 0 | 7 | 0 | 2 |
| `System.TimeSpan::FromMilliseconds(double)` | 17 | 7 | 0 | 8 | 2 | 0 |
| Sum | 62 | 43 | 0 | 15 | 2 | 2 |

- The constants are literals (26), a `const` field (12 calls of `FromHours`) and `-1` (5 calls of
  `FromMilliseconds`, a negated literal). Every one is of type `int`. The largest is 30000.
- No call passes an `int` of hours or days, the two factories whose range an `int` can leave. Every
  `int` that is not constant is a count of seconds or milliseconds, and every `int` of those is in
  the range of `TimeSpan`.

### Criterion 2: an entry whose adapter states the range
- Bar test (`equiv-adr`): first row. ADR 0020 already decides how two members are equated and
  already leaves a call alone when the adapter cannot address it; the range is that decision
  applied to a case it did not spell out. Recorded as a dated clarification in ADR 0020
  (2026-10-08), with VERIFICATION-MODEL section 3 and the catalogue's header saying the same.
- Decision: how two callees that agree on a range of one argument are equated -> an entry whose
  adapter item states the range (`"integer": {"bits", "min", "max"}`) and addresses a call only when
  the frontend knows the argument is inside it. Alternatives: both callees modelled as one pure
  function of the integer with each side's exception type outside the range. Rule: 4. The model
  would put two library members' bodies into both lowerings, and on `jellyfin-13023` it would
  decide no call the entry form does not: the only calls left are the 2 that pass a `long`, where
  the `double` overload can lose bits, so the two are not one function even in range.
- Decision: what "known to be inside the range" is -> a compile-time constant inside it, or an
  argument that is not constant, of a type all of whose values are inside it. Alternatives:
  constants only. Rule: 3. Criterion 4 allows a non-constant argument the decision covers soundly,
  an `int` of seconds, minutes or milliseconds always is, and it is 15 of the 62 calls. A test pins
  each type against each range.
- Decision: the ranges -> the range of `TimeSpan` for days (-10675199 to 10675199) and hours
  (-256204778 to 256204778); every 32-bit integer for minutes, seconds and milliseconds.
  Alternatives: the range of `TimeSpan` for all five. Rule: 4. For these three a large `long`
  loses bits in the `double` product before the range ends (for minutes, above about
  3.8 x 10^9), so the two overloads differ inside the range of `TimeSpan` too; the 32-bit range
  is inside the exact part for all three and is the type of every call counted.
- Decision: an entry names the runtime that added its modern member (`addedIn`) and applies only to
  a pair whose runtimes cross it -> `net9.0` on five entries, `net10.0` on one. Alternatives: apply
  on every pair, as the older entries do. Rule: 3. On a .NET Framework 4.8 to .NET 8 pair both
  sides bind the `double` overload; a rewrite there would bind the legacy side to a member the
  modern side cannot call, and every such call would become a rebound call (ADR 0042's stated cost
  of rewriting one side). That would turn pairs that are compared today into Unknown.
- Surprise: .NET 10 added `TimeSpan.FromMilliseconds(long)`, so an integer binds
  `FromMilliseconds(long, long)` on .NET 9 only and the one-parameter overload from .NET 10 on. The
  five pairs are six entries: `bcl.timespan-from-milliseconds-integer` (`net10.0`, first in the
  file) and `bcl.timespan-from-milliseconds-integer-and-microseconds` (`net9.0`, with the constant
  0). A pair whose modern side has both takes the first; one that ends at .NET 9 only crosses the
  second's runtime. Not covered, and not one of the five pairs: on a .NET 9 to .NET 10 pair
  `FromMilliseconds(long, long)` with its default against `FromMilliseconds(long)`, which is a
  rebound call today. No ticket is filed for it: no corpus pair runs from .NET 9 to .NET 10.

### Criteria 3 and 4: tests and sample
- `ApiIntegerRangeTests` (Core: the six entries and their ranges, the parser, `AppliesWithin`),
  `IntegerRangeLoweringTests` (a constant in range for each of the five, constants at both ends
  and one past them, each integral type against each range, the widening, the runtime gate).
- `samples/timespan-integer-overloads`, legacy .NET Framework 4.8 and modern .NET 10. Equivalent
  (`bounded`) with the entry applied and no `reboundCalls`: `Week` (days), `CacheLifetime` (hours,
  a `const` field), `Retry` (minutes), `Poll` (seconds), `Never` (milliseconds, `-1`) and `Wait`
  (an `int` of seconds). Unknown (`opaque`, scope `line`) with the two callees in `reboundCalls`:
  `TooLong` (a constant one hour past the range), `Hours` (an `int` of hours) and `Delay` (a `long`
  of milliseconds). `Backoff` passes 1 minute on the legacy side and 2 on the modern side and is
  Divergent with the entry applied.
- The IL lowering applies no catalogue entry, and `IlLoweringParityTests` lowers each body as a
  same-runtime pair, which crosses no runtime, so the sample's methods needed no entry in its known
  list.

### Criterion 5: `jellyfin-13023` again
- Re-run with the entries, `--lower-only`, same checkout and config. Of 162 changed pairs:

| | Before | After |
|---|---|---|
| Changed pairs that hold `rebound-call` | 69 (42.6%) | 25 (15.4%) |
| Changed pairs it alone keeps opaque | 12 (7.4%) | 0 |
| Changed pairs without opaque | 23 (14.2%) | 35 (21.6%) |
| Bodies that hold `rebound-call`, per side | 71 | 27 |

- The fall is 44 pairs, 27.2% of the changed pairs, where the criterion asks for 5% (9 pairs).
  The 12 pairs it alone kept opaque all lower now, and matched pairs, congruent pairs (14,370) and
  changed pairs are unchanged.
- The census does not say which callee pairs the 25 hold. By P2-137's table they are the `params`
  span pairs (P2-143) and the six pairs under its floor, and by criterion 1 the 2 calls that pass a
  `long` of milliseconds.

### Other
- The census runs are the skill's `census` mode, which takes no compare mode. They read this
  worktree's own checkouts of the commits `tools/corpus/pairs.csv` pins, one run at a time.
