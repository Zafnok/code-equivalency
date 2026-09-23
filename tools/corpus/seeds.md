# Seeded behaviour changes (ADR 0028, seeded recall)

`seeded` mode injects known behaviour changes into a copy of a pair's modern side. It then checks
that `equiv` never calls a seeded method Equivalent. Seeded recall must be 100%. A seed reported
Equivalent is a soundness bug and preempts all other work.

## Rules

1. Seed only methods the migration left textually unchanged. That way the seed is the only
   difference, and the expected verdict is unambiguous.
2. At most one seed per method, and 5 to 10 seeds per pair, spread across seed ids and files.
3. Every seed must be observable. Write down a concrete input and the two outcomes (legacy, seeded).
   If you cannot, pick another site. A change in dead code, or one whose value is never observed,
   is not a seed.
4. Record each seed in `.corpus/pairs/<slug>/seeds.json` as
   `{id, file, line, identity, input, legacyOutcome, seededOutcome}`. That file stays in `.corpus/`
   because it quotes third-party code. The committed SUMMARY.md lists only seed id, identity and
   verdict.
5. The expected verdict is Divergent, or Unknown with a `relatedLocation` on the seeded line.
   An Unknown that does not point at the line still counts toward seeded recall, and is also
   reported as a blast-radius miss.

## Catalogue

| Id | Change | Why it is realistic |
|---|---|---|
| S01 | `Comparer<string>.Default` / `StringComparer.CurrentCulture` swapped with `StringComparer.Ordinal` (or `StringComparison` swapped likewise) | Git Extensions #8522 made this edit in a "no functional change" migration. |
| S02 | An argument `x` becomes `x ?? string.Empty` (or a `?? default` is dropped) | Same PR; nullable-annotation clean-ups do this. |
| S03 | A relational operator's bound shifts by one (`<` to `<=`, `>` to `>=`) | Classic slip in loop rewrites. |
| S04 | `Math.Round(x, n)` becomes `Math.Round(x, n, MidpointRounding.AwayFromZero)`, or the reverse | Mirrors the `business-layer` sample's divergence. |
| S05 | A guard `if (x == null) throw new ArgumentNullException(...)` is deleted | Agents drop "redundant" checks under nullable reference types. |
| S06 | Two consecutive calls with side effects are swapped | Observable through the call trace (ADR 0018). |
| S07 | An integer literal changes by one | Stands in for a mis-transcribed constant. |
| S08 | A thrown exception type changes (`ArgumentException` to `InvalidOperationException`) | Exception type is observable; the message is not. |
| S09 | A field or property write is deleted | The final heap is observable (ADR 0018). |
| S10 | `x.ToString()` on a number becomes `x.ToString(CultureInfo.InvariantCulture)` | Common analyzer-driven edit that changes output under a non-invariant culture. |
