# P2-139 The interpolated strings P2-086 left opaque are counted, and the largest form without a ticket lowers
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-086 lowers an interpolated string whose holes are `string` or 8- to 64-bit integers with no
format or alignment clause, and is done. Every other interpolated string is an opaque node with
reason `InterpolatedString`. P2-102 owns one of the forms left, an integer hole before a hole that
runs code. Nobody has counted the others. P1-028's row (`docs/runs/2026-10-07-opaque-tail.md`),
over the three large runs' 2,246 changed pairs, all three made after P2-086:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `InterpolatedString` | 74 / 74, 21, 4 | 87 / 81, 87, 23 | 31 / 31, 4, 0 | 112 | 27 (1.2%) | 59 (2.6%) |

47 of the 59 are on `gitextensions-9860`, where every body that holds the reason is a changed pair
(87 of 87): net5.0 binds an interpolated string to `string.Format` and net6.0 to
`DefaultInterpolatedStringHandler`, so the pair is never congruent, and the reason alone is 3.2% of
that pair's changed pairs. The later censuses in the same report give 54 of 1,743 (3.1%). Count the
forms, and lower the largest one P2-102 does not own.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `InterpolatedString`, `Interpolation`,
`InterpolatedStringText`; ADR 0034 (per-ticket unlock rule);
`docs/tickets/done/P2-086-interpolated-string-owner.md` (its split and what it proved the two
bindings share); `docs/tickets/P2-102-integer-hole-before-a-hole-that-runs-code.md`;
`docs/tickets/P2-094-precision-string-format-against-interpolated-string.md`;
`docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `InterpolatedString` opaque nodes of `gitextensions-9860`'s changed
   pairs (a `--lower-only` run) by the first thing that keeps the string out of P2-086's rule: a
   hole with a format clause, a hole with an alignment clause, a hole of another type by kind
   (`bool`, `char`, an enum, floating point or `decimal`, `DateTime` or another struct, `object` or
   another reference type), an integer hole before a hole that runs code (P2-102), a string
   converted to `FormattableString` or `IFormattable`, a custom handler, other. For each form give
   the nodes, the changed pairs it is in, the changed pairs it alone keeps opaque, and the ticket
   that owns it or "none". Counts in `## Notes` (hole type kinds and clause kinds only).
2. The form with the most changed pairs alone and no owning ticket lowers to IR as what both
   bindings compute, by P2-086's rule: the lowering is used only where `string.Format` and
   `DefaultInterpolatedStringHandler` give the same string and make the same calls in the same
   order, and `## Notes` says why they do for this form, including the current culture. Decide the
   representation with `equiv-decide` and log it.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator for both bindings, and the
   `IOPERATION-COVERAGE.md` rows updated.
4. On a re-run, the changed pairs of `gitextensions-9860` that hold `InterpolatedString` fall by at
   least 2% of changed pairs, or `## Notes` records why not.
5. Each remaining form at or above 5% of changed pairs alone and with no owner is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that P2-102's form holds more than two thirds of the changed pairs alone, or
that the largest form is one where the two bindings do not compute the same string, lower nothing:
record the split and stop. More than two forms lowered means the ticket was misread.

## Out of scope
P2-102's form and its culture assumption. `string.Format` written out against the interpolated
string it becomes (P2-094). The IL fallback.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): fourth by marginal unlock.
- **Criterion 1: the split (2026-10-11).** Census (`--lower-only`) of `gitextensions-9860` (legacy
  `bcd0c2617bdd`, modern `37797ea4dd74`) at equiv `feee17e1`, exit 0, 106 s: 14,073 matched pairs, 13,468
  congruent, 605 changed, 285 without opaque. `InterpolatedString` is in 87 legacy and 80 modern bodies and in
  87 changed pairs, and it is the only reason in 43 of them (7.1%). The split comes from a second census with a
  throwaway build that named each opaque string's form in its reason; that build was never committed and its
  output stays under `.corpus/`. A string is filed under the first thing, in hole order, that keeps it out of
  P2-086's rule (a clause before the hole's type). "Bodies" counts a body once per form it holds, so the column
  sums to more than 87. "Alone" is the changed pairs whose every opaque is a string of that form; the 43rd pair
  holds two forms and nothing else.

  | Form (first thing that keeps the string opaque) | Bodies (legacy / modern) | Changed pairs in | Alone | Owner |
  |---|---|---|---|---|
  | A hole of a sealed class that is not `IFormattable` | 27 / 24 | 23 | 15 (2.5%) | none: this ticket |
  | A hole with a format clause (on a `DateTime` 7 / 6, floating point 5 / 5, a `Guid` 1 / 1, an integer or `string` 7 / 6) | 20 / 18 | 17 | 8 (1.3%) | none |
  | A hole with an alignment clause | 0 / 0 (1 / 1 behind a format clause) | 0 | 0 | none |
  | A hole of another reference type: a class that is not sealed (16 / 16) or a sealed `IFormattable` one (1 / 2); no `object`, interface or array | 17 / 18 | 14 | 5 (0.8%) | none |
  | A hole of a struct other than `DateTime` (`Guid`, `Point`, the pair's own structs) | 8 / 8 | 8 | 5 (0.8%) | none |
  | A `char` hole | 6 / 6 | 6 | 3 (0.5%) | none |
  | An integer hole before a hole that runs code | 10 / 7 | 10 | 3 (0.5%) | P2-102 |
  | An enum hole | 4 / 3 | 4 | 2 (0.3%) | none |
  | A `DateTime` hole | 5 / 1 | 5 | 1 (0.2%) | none |
  | A `Nullable<T>` hole | 7 / 7 | 7 | 0 | none |
  | A `bool` hole | 2 / 1 | 2 | 0 | none |
  | A floating point hole (no `decimal`) | 2 / 2 | 2 | 0 | none |
  | A hole of a type parameter | 2 / 2 | 2 | 0 | none |
  | A string converted to `FormattableString` or `IFormattable`; a custom handler; other | 0 / 0 | 0 | 0 | n/a |

  The Size guard does not trip: P2-102's form holds 3 of the 43 pairs, and the largest form is one where the
  two bindings compute the same string (next note).
- Decision: which form is "the largest with no owner" -> a hole of a sealed class that does not implement
  `IFormattable`, 15 pairs alone, taken out of the ticket's kind "`object` or another reference type" (20 alone)
  because it is the part of that kind where both bindings provably make the same call. Alternatives: the whole
  kind (for a class that is not sealed the run-time type decides: a derived `IFormattable` is formatted through
  `IFormattable.ToString(null, null)` by `string.Format` and through `ISpanFormattable.TryFormat` by the handler
  when it has one, and through neither `ToString()`; that is the Size guard's "do not compute the same string");
  the format clause (8 alone, and a `DateTime` or floating point format is culture-sensitive text). Rule: 3 (the
  form criterion 2's condition can be pinned for).
- **Why the two bindings agree for this form, the current culture included.** The value of a hole whose static
  type is a sealed class is that class exactly, or null. With no provider, `string.Format` (.NET Framework 4.8
  and .NET 5 alike) asks `arg is IFormattable` and otherwise calls `arg.ToString()`, appending nothing for a null
  argument or a null result; `DefaultInterpolatedStringHandler.AppendFormatted<T>` asks `value is IFormattable`
  and otherwise appends `value?.ToString()`, nothing when that is null. The class is not `IFormattable`, so both
  take the second path: the same virtual call of the method in `object`'s `ToString()` slot, or no call. Neither
  binding hands that call a culture or reads one for it; whatever the method reads, it reads at the same place
  in both. What differs is when: `string.Format` formats after every hole is evaluated, the handler before the
  next one is. So the rule asks that every hole after the first such hole be a constant, or a local or parameter
  passed by value that no lambda, local function or query in the body names; the call cannot change those, so
  the two orders are one. An integer hole next to it keeps P2-086's rule, and is formatted after the class hole
  before it and before the class hole after it under both bindings.
- Decision (criterion 2): the representation -> a branch on the hole's nullness joining `null` with the call of
  the class's own `ToString()` on it, the join handed to the `Concat` chain P2-086 builds, which appends null as
  nothing. The call is the one a written `o.ToString()` makes (same identity, `threw` edge and heap pairs; not
  closed), so `$"{o}"` is `"" + o?.ToString()`. Alternatives: one `IrPure` per hole (a catalogue entry, and a
  `ToString()` that writes the heap is no pure function); the call with no null test (a null hole would throw
  where neither binding does). Rule: 1.
- Decision (`equiv-adr` bar test): no IR node, no new assumption, no verdict change -> a lowering rule, in
  VERIFICATION-MODEL section 3 and `IOPERATION-COVERAGE.md`. ADR 0044 is not leaned on: it is about what a
  later hole does to integer formatting, and here nothing runs between a class hole's evaluation and its call
  that the other binding orders differently.
- Decision: a hole after the first sealed class hole that reads a captured variable keeps the string opaque,
  found by name -> any identifier token of that name inside a lambda, anonymous method, local function or query
  of the declaring body. Alternatives: `SemanticModel.AnalyzeDataFlow` (exact, and a second semantic pass per
  string); allow it (a lambda that writes a local is an opaque delegate, but the local keeps its value in the
  IR, so `$"{o}{n}"` would be one body where `o.ToString()` runs that lambda and the bindings read `n` on either
  side of it). Rule: 4.
- Decision: both bindings in the oracle -> `LoweredIrAgreesWithCompiledCSharp` is a theory over the language
  version, C# 9 (`string.Format`) and the default (the handler), compiling and lowering the whole generated
  class under each; the generator's `Cell` is sealed and its `ToString()` now adds 1 to `G`, so the final `G`
  shows the call ran once under either. Alternatives: a second, smaller oracle for the one statement. Rule: 4.
- Deviation: `docs/VERIFICATION-MODEL.md` (section 3) and `tests/Equiv.TestSupport/LoweringOracleGen.cs` are
  outside the ticket's Files; `equiv-extend-ir` step 1 and criterion 3 need them.
- **Criterion 4: the re-run (2026-10-11).** Same checkouts, this branch, exit 0, 104 s.

  | `gitextensions-9860` | Before | After |
  |---|---|---|
  | Matched / congruent / changed pairs | 14,073 / 13,468 / 605 | the same |
  | Bodies holding `InterpolatedString` (legacy / modern) | 87 / 80 | 71 / 64 |
  | Changed pairs holding it | 87 (14.4%) | 71 (11.7%) |
  | Changed pairs it alone keeps opaque | 43 (7.1%) | 31 (5.1%) |
  | `changedPairsWithoutOpaque` | 285 | 297 |
  | Lowerable share (ADR 0034) | 47.1% | 49.1% |

  The fall is 16 changed pairs, 2.6% of 605, so criterion 4 holds. 12 of the 16 are now opaque-free; the other
  4 keep their other reasons. Of the form's 15 pairs alone, 3 stay: their class hole is followed by a hole that
  reads a field or a property or runs code. A third census with the throwaway names shows what is left is that
  and the other forms of the table, unchanged; no string of another form was lowered.
- **Criterion 5.** 5% of 605 changed pairs is 31. No form reaches it alone (the largest left is the format
  clause, 8), so no ticket is filed. `InterpolatedString` as a whole is still at 5.1% alone on this pair, spread
  over the forms above; P2-102 owns 3 of those pairs.
- A pair is lowerable, not decided: the `ToString()` call's result is the solver's to choose, as any call's is.
  No verifying run was made.
