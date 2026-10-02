# P2-086 An interpolated string no longer keeps a changed pair opaque
Status: done (PR #334)
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: none

## Goal
`InterpolatedString` is an opaque node. Alone, it keeps this share of changed pairs opaque:
- `eshop-manual`: 5 of 26 (19.2%), P2-065's run;
- `gitextensions-8522`: 26 of 1,143 (2.3%), P2-046's run;
- `duplicati-3124`: 20 of 936 (2.1%), P2-065's census.

ADR 0028 wants an owner for a reason at or above 5% of a pair's changed pairs. P2-046 named ADR
0039's IL fallback as the owner. P1-018 has since measured the fallback and left it off by default,
so the reason has no open owner.

`IOPERATION-COVERAGE.md` says why a migration hits it: the same interpolated string binds to
`string.Format` on .NET Framework and to `DefaultInterpolatedStringHandler` on modern .NET, so the
two fragments fingerprint differently and are not shared (M4-004). Unchanged source text therefore
becomes a changed, opaque pair exactly when the runtime changes. Lower an interpolated string so that
the same text and the same holes are the same value on both sides whichever way it binds.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `InterpolatedString`, ADR 0024 (shared opaque fragments),
ADR 0041 (closed calls; `string + string` is already `System.String::Concat`), ADR 0034 (per-ticket
unlock rule), `docs/runs/2026-10-01-full-eshop-manual/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `InterpolatedString`-only changed pairs of `eshop-manual` and
   `duplicati-3124` (a `--lower-only` run each) by shape: holes of string type only, holes with a
   format or alignment clause, holes of other types, a handler other than the default one. Counts in
   `## Notes`.
2. Route the representation through `equiv-decide`, and through `equiv-adr`'s bar test if it adds an
   IR node or a culture assumption. Log the `Decision:` line. A format or alignment clause, or a hole
   whose formatting depends on the current culture, stays opaque unless the decision covers it.
3. The chosen shapes lower to the same IR for a `string.Format` binding and for a handler binding of
   the same text. Snapshot test with both bindings; the `IOPERATION-COVERAGE.md` row is updated.
4. On a re-run, `changedReasonSets["InterpolatedString"]` on `eshop-manual` falls, or `## Notes`
   says why not. The figures for all three pairs go in `## Notes`.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows most holes carry a format clause or a non-string type, stop after criterion 2
and record the finding.

## Out of scope
The IL fallback. `string.Format` calls written by hand.

## Notes
- Found by P2-065 (`docs/runs/2026-10-01-migrations-verdict.md`).
- **Criterion 1: the split (2026-10-01).** Census (`--lower-only`) of both pairs at equiv `26913ea`, with
  a throwaway build that recorded each `InterpolatedString` opaque's binding, parts and hole types, and
  each changed pair's reason set. That build was never committed, and its output stays under `.corpus/`.
  `eshop-manual`: exit 5 (the three P2-083 crashes), 23 s; 26 changed pairs of 119, 17 without opaque,
  `changedReasonSets["InterpolatedString"]` 5. `duplicati-3124`: exit 5 (the eight P2-083 crashes), 90 s;
  936 changed pairs of 6,275, 505 without opaque, `changedReasonSets["InterpolatedString"]` 25 (20 in
  P2-065's census at `ef79ff6`, before P2-067), 46 changed pairs holding the reason at all.

  | Shape of an `InterpolatedString`-only changed pair | `eshop-manual` (5) | `duplicati-3124` (25) |
  |---|---|---|
  | Holes of `string` type only | 1 | 13 |
  | A hole with a format or alignment clause | 0 | 0 |
  | A hole of another type | 4 | 12 |
  | A handler other than the default one | 0 | 0 |

  The "other type" pairs: on `eshop-manual` all 4 hold `int` holes and nothing else that is not `string`;
  in 2 of them every `int` hole is a parameter, and in 2 an `int` hole is followed by a hole that calls
  a method. On `duplicati-3124`, 10 hold only `int` or `long` besides `string` (in 2 of them the hole is
  an `(int)` cast of a `double` expression) and 2 a `System.TimeSpan`. By string, per side:
  `eshop-manual` 14 (7 with `string` holes only, 7 with an `int` hole), `duplicati-3124` 45 (30 and 15).
  By hole, per side: `eshop-manual` 12 `int` and 7 `string`; `duplicati-3124` 47 `string`, 13 `int`,
  5 `long`, 2 `System.TimeSpan`.
- **What the split says.** No hole in either pair has a format or alignment clause, and no string is
  handed to a custom handler. On `eshop-manual` every string binds `string.Format` on the legacy side
  and `DefaultInterpolatedStringHandler` on the modern side, as the Goal says. On `duplicati-3124` both
  sides bind `string.Format` (the modern side is .NET 5, which has no handler type), 43 of the 45
  strings on each side carry a fingerprint (the other 2 cast a `double` to `int`, which is
  runtime-sensitive), and each of the 25 pairs changed somewhere else: it is counted under the reason
  because a fragment that both sides share is still an `IrOpaque`, as P2-067 found for delegates. A
  hole's IOperation is the same under both bindings: the `string.Format` binding has no conversion to
  `object` around an `int` hole.
- Decision: the Size guard ("most holes carry a format clause or a non-string type") -> read over the
  two measured pairs together, where 32 of 86 holes (37%) are not `string` and none has a clause, so
  it does not trip. Alternatives: read per pair, where it trips on `eshop-manual` alone (12 of 19 holes
  are `int`) and the ticket would close with the reason still at 19.2% there and no owner; that reading
  is also moot once the decision below covers an integer hole without a culture assumption. Rule: 3
  (the reading criteria 3 and 4 can then pin).
- Decision (criterion 2): an interpolated string whose holes are covered -> the left fold of closed
  `System.String::Concat(string,string)` calls over its parts, exactly as `a + b` lowers (ADR 0041): a
  text part is its constant, a `string` hole its value, and a hole of an 8- to 64-bit integer type the
  closed call to that type's parameterless `ToString()`. One part alone is concatenated to `""`, since
  `$"{s}"` is never null. Alternatives: one `IrPure` per interpolated string named by its text (a new
  catalogue entry, and `$"a{s}"` would not equal `"a" + s`); a call to `String.Format` (the modern
  side never calls it). Rule: 1 (it is the call the compiler makes for `string` holes under both
  bindings, and the lowering `a + b` already has).
- Decision (criterion 2, `equiv-adr` bar test): no IR node and no culture assumption are added, so no
  ADR and no clarification; the rule is a lowering rule and goes in VERIFICATION-MODEL section 3 and
  `IOPERATION-COVERAGE.md`. An integer hole is formatted with the current culture under both bindings
  (`string.Format` calls `IFormattable.ToString(null, null)`, the handler `ISpanFormattable.TryFormat`
  with no provider), which is what `i.ToString()` does, and `System.Int32::ToString()` is already a
  closed call whose result is a function of its position (ADR 0041: "the callee may still read ambient
  state such as the current culture"). So the culture stays where the model already keeps it.
  Alternatives: treat an integer's text as culture-free (a culture assumption, and false for the
  negative sign); keep integer holes opaque (4 of `eshop-manual`'s 5 pairs keep the reason). Rule:
  `equiv-adr` bar test.
- Decision: which strings are covered -> every hole is of type `string` or of an integer type
  (`sbyte` to `ulong`), none has a format or alignment clause, and every hole after the first integer
  hole reads only a local, a parameter, a constant or a field of `this`. The last condition is where
  the two bindings really differ: `string.Format` formats after every hole is evaluated, the handler
  formats each hole before the next is evaluated, so `$"{i}{F()}"` formats `i` under the culture `F`
  leaves on .NET Framework and under the one it finds on .NET 10. With no call and no throw after the
  first integer hole the two orders are one. Everything else stays opaque with reason
  `InterpolatedString`, fingerprinted per binding as before. Alternatives: assume a hole does not
  change the current culture (a new assumption in VERIFICATION-MODEL section 1, hence a new ADR; it
  would cover the 2 `eshop-manual` pairs whose `int` hole is followed by a `Count()` hole); lower each
  binding in its own order (the same text would then differ in trace order and be Divergent). Also
  left opaque: `bool`, `char`, enum, `Nullable<T>`, floating point and `decimal` holes (none
  measured), and any other type, such as the 2 `System.TimeSpan` holes, whose `ToString`,
  `IFormattable` and `ISpanFormattable` members are three different callees. Rule: 4.
- **Criterion 3.** `InterpolatedStringLoweringTests.BothBindingsLowerToTheSameBody` compiles one text
  under C# 9 (`string.Format`) and under C# 10 (the handler), checks that the two bound fingerprints
  differ and the two IR dumps are equal, and snapshots the dump. The other tests of that class hold the
  lowering to the `+` chain's: `$"a{s}b{t}"` dumps as `"a" + s + "b" + t` does, and `$"n={i}"` as
  `"n=" + i.ToString()` does. So a pair that rewrites a concatenation as an interpolated string, which
  P2-048's `ConcatToInterpolation` seeds do, is now two equal bodies.
- **Criterion 4: the censuses after the change (2026-10-01).** Same checkouts, this branch.
  `eshop-manual`: exit 5 (the same three crashes), 11 s. `duplicati-3124`: exit 5 (the same eight),
  61 s. `gitextensions-8522` (legacy `3f4ed21998af`, modern `5190ba5c1a5f`): exit 0, 159 s; its
  "before" column is P2-067's census after its own change (exit 0, 232 s), which is `main`'s lowering.

  | | `eshop-manual` | `duplicati-3124` | `gitextensions-8522` |
  |---|---|---|---|
  | Changed pairs | 26 | 936 | 1,294 |
  | `changedReasonSets["InterpolatedString"]` before | 5 (19.2%) | 25 (2.7%) | 45 (3.5%) |
  | `changedReasonSets["InterpolatedString"]` after | 2 (7.7%) | 2 (0.2%) | 7 (0.5%) |
  | Changed pairs holding the reason at all, before to after | 5 to 2 | 46 to 6 | 100 to 23 |
  | Bodies holding the reason (legacy / modern), before | 5 / 5 | 69 / 69 | 331 / 329 |
  | Bodies holding the reason (legacy / modern), after | 2 / 2 | 10 / 10 | 74 / 74 |
  | `changedPairsWithoutOpaque` before to after | 17 to 20 | 505 to 527 | 667 to 705 |
  | Lowerable share (ADR 0034) before to after | 65.4% to 76.9% | 54.0% to 56.3% | 51.5% to 54.5% |

  Matched, congruent and changed pairs are the same before and after on all three, as they must be:
  congruence is decided on the bound fingerprint, which still differs per binding. A pair that leaves
  the set does not always become opaque-free: a hole is now lowered, so an opaque construct inside one
  (a conversion, a lifted operator) now shows under its own reason, and a pair of a mixed set moves to
  the set of its other reasons.
- **`eshop-manual` is still over the 5% line: 2 of 26 (7.7%).** That is the number of pairs the split
  found whose `int` hole is followed by a hole that calls `Count()`, the shape the last decision above
  leaves opaque. Covering it takes the assumption that evaluating a hole does not change the current
  culture, which is a new line in VERIFICATION-MODEL section 1 and so a new ADR. Filed as P2-089,
  which is the owner ADR 0028 asks for. The reason is under 1% on the two large pairs; the 2 pairs
  left on `duplicati-3124` are the number the split found with a `System.TimeSpan` hole.
- **What this does not change.** A pair is lowerable, not decided. Each `Concat` and `ToString` is a
  closed call with a `threw` edge whose result the solver chooses (ADR 0041), exactly as in a `+`
  chain, and P2-081 owns a Divergent that rests on a result the real member cannot give. No full run
  was made.
- Roslyn already folds an interpolated string of constant `string` holes to a constant under both
  language versions (`AStringOfConstantsIsAConstant`), and converts a hole with no type of its own
  (`null`, `default`) to `string` under both (`AHoleWithNoTypeOfItsOwnIsAStringHole`), so neither
  needed code.
- Outside the ticket's Files, what pinned `$"{count}"` or a string-only interpolated string as opaque
  had to follow. `samples/unknown-new-throw` needs an unshared opaque on the modern side, so its
  string is now `$"{count:D}"`; its `expected.sarif.json` changes only the region's end column, and no
  verdict changes. `PairGenLoweringTests` now asserts no opaque at all on a `ConcatToInterpolation`
  pair. `samples/business-layer` is unchanged: `Describe`'s `int` hole is a property read followed by
  another property read, so it stays opaque. VERIFICATION-MODEL section 3 has the rule
  (`equiv-extend-ir` step 1), and `IOPERATION-COVERAGE.md` gains rows for `InterpolatedStringText` and
  `Interpolation`.
