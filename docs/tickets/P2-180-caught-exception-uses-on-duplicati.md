# P2-180 The uses of a caught exception that keep Duplicati's changed pairs opaque are counted, and the largest one lowers
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-140 made a `catch` variable that is only read as `e.Message` a value, and left every other use of a
caught exception an opaque node with reason `CaughtException`. It measured the uses on
`gitextensions-8522` only, where no other use reached 1% of changed pairs, and filed nothing.

P2-130's run of `duplicati-3124` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-13-full-duplicati-3124/SUMMARY.md`), the pair's first run to a result, has
`CaughtException` alone in 37 of 707 changed pairs (5.2%), with `rethrow` in 19 more, and in 45
Unknown results (6.4% of changed pairs): the pair's largest opaque reason, above ADR 0028's 5%, and no
open ticket owns it. `rethrow` (in 31 Unknown results, 4.4%) comes with it.

When done, the use of a caught exception that keeps the most of Duplicati's changed pairs opaque is
lowered, or decided to stay opaque, with the count that chose it.

## Spec references
`docs/tickets/done/P2-140-*.md` (its table of uses and its decisions), ADR 0018 and its clarification
for the thrown exception, `IOPERATION-COVERAGE.md` (`CaughtException`, `rethrow`),
`.claude/skills/equiv-extend-ir`, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `duplicati-3124` at `main`'s head, P2-140's table of uses (and of raises whose exception
   is not a value), plus `rethrow`, over the changed pairs: in, and alone.
2. Lower the largest use that is not already lowered, or record with `equiv-decide` why it stays
   opaque and lower the next.
3. Rerun `duplicati-3124` in compare mode quick and record, in Notes, `CaughtException` alone and in
   Unknown results next to 37 and 45.

## Tests
Lowering tests in `tests/Equiv.Frontend.CSharp.Tests`, and a pair test in
`tests/Equiv.Tests.Integration/CaughtExceptionEquivalenceTests.cs`, for the lowered use, including two
bodies that differ in what they do with the exception and must not prove.

## Size guard
A new IR node or sort: stop and route it through `equiv-adr`.

## Out of scope
Stack traces; exceptions handed to a logger.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): the largest opaque reason of `duplicati-3124` with no open
  owner.
- **Criterion 1: the count (2026-10-17).** Census (`--lower-only`, committed config
  `tools/corpus/configs/duplicati-3124.json`) of `duplicati-3124` (legacy `beaf03562fdc`, modern `4f565a004f52`) at
  `main`'s head `82ba9c14` built with a throwaway change that named, in each `CaughtException` reason, every use of
  the clause's variable and its declared type, and in each raise that is not a value its exception type; it also
  treated every clause as naming its exception, so that the raises a clause would then meet show up as well (never
  committed; its output stays under `.corpus/`). Exit 0, 80 s. 6,294 matched pairs, 640 changed, 370 without opaque.
  `CaughtException` is in 107 changed pairs (16.7%) and the only reason in 35 (5.5%), the figure the 2026-10-17 run
  summary gives; `rethrow` is in 52 (8.1%) and the only reason in 5 (0.8%). A pair counts under each use its
  variables have. "Alone" is the changed pairs whose every opaque is a `CaughtException` and whose variables have that
  one use besides `Message`, which P2-140 lowered. "Opaque-free" is the changed pairs that would have no opaque at all
  were that use lowered, counting the raises that are not values the newly named clauses would then meet.

  | Use of the variable | Changed pairs in | Alone | Opaque-free if lowered |
  |---|---|---|---|
  | inner exception, an argument of a new exception's constructor | 43 | 17 (2.7%) | 10 (1.6%) |
  | argument of a call | 47 | 9 (1.4%) | 1 (0.2%) |
  | property read `Message`, next to another use | 31 | 0 | 0 |
  | property read `Response` (`WebException`) | 18 | 0 | 0 |
  | type test or cast | 9 | 0 | 0 |
  | assignment to a local | 5 | 1 | 0 |
  | a call on it (`ToString` 4, `Flatten` 1) | 5 | 1 | 0 |
  | property read `InnerException` | 3 | 0 | 0 |
  | another property read (`StatusCode` 2, `ErrorCode`, `Status`, three of library types) | 8 | 0 | 0 |
  | operand of a binary operator, a variable initializer, a write to the variable, a `when` filter | 1 each | 0 | 0 |

  Raises whose exception is not a value, met by a clause that already names its exception:
  `System.NullReferenceException` (the null test of a receiver) in 8 changed pairs, alone in 2; `System.Exception`
  from a closed call or a pure function's flag in 4, alone in none; several kinds together alone in 1. The other 4
  pairs alone hold two uses each: an argument of a call with a type test, a variable initializer or an inner
  exception, and `ToString` with a `when` filter. The 43 changed pairs with an inner exception, by the declared type
  of the variable (a pair counts once per type): `System.Exception` 17, `WebException` 13, and 14 over eight library
  and Duplicati exception types. The arguments of a call are, all but a handful, a log or console
  write.
- Decision: which use lowers -> the inner exception (largest alone, 17; and 10 changed pairs it would leave with no
  opaque), and of it only `throw new T(..., e)`, a constructor argument whose new exception is thrown at once.
  Alternatives: the argument of a call (second, 9 alone, 1 opaque-free, and nearly every one a logger, which this
  ticket puts out of scope); any exception constructor, thrown or not (a new exception handed to a logger reads the
  stack trace as the caught one would). Rule: 2.
- Decision: why a thrown inner exception is a value -> its stack trace leaves the body only inside the exception
  thrown, which VERIFICATION-MODEL section 1 observes by its type, or reaches an enclosing clause of the same body,
  which the same rule governs again; the constructor call is then compared on its arguments as any call is. Bar
  test: row 1, a clarification of ADR 0018 (2026-10-17) in this PR: no IR node, sort or `threw` edge changes.
  Alternatives: a new ADR (no Decision of ADR 0018 changes); keep the inner exception opaque (no other use reaches 2%
  alone). Rule: 1.
- Decision: the implicit conversions in the way -> C# puts one between the variable and the parameter when their
  types differ, and one between `new T(...)` and the `throw` when `T` is not `System.Exception`; both are looked
  through, an explicit cast is not. Rule: 4.
- **Criterion 3: the rerun (2026-10-17).** `duplicati-3124`, full, compare mode quick, `--jobs 4`, committed config,
  this branch (`82348af0`, `src/` as `main` plus this change), 714 s, exit 1 (Divergent results exist, as before).
  640 changed pairs.

  | | 2026-10-13 (`e98fd1b9`, P2-130) | 2026-10-17 (`de93b9c7`, P2-237, `src/` as `main`) | this branch |
  |---|---|---|---|
  | changed pairs | 707 | 640 | 640 |
  | `CaughtException` alone | 37 (5.2%) | 35 (5.5%) | 25 (3.9%) |
  | `CaughtException` in, census | n/a | 107 | 96 |
  | Unknown results whose opaque reasons hold `CaughtException` | 45 | 46 | 38 (5.9%) |
  | changed pairs the solver proves | 189 | 213 | 221 |

  Against P2-237's run, the same `src/` but for this change (that run also passed `--execute`), result by result: 7 Unknown `opaque:CaughtException` are now Equivalent (bounded), 6 of them
  methods that wrap a backend's exception in `FileMissingException` or `FolderMissingException` the same way on both
  sides (SSHv2 `Delete` checked by hand: both bodies are equal), and the seventh a decryption that wraps a
  `CryptographicException`; one Unknown keeps `call-throw-in-try` and `rethrow` and loses `CaughtException`; one
  timeout is now proved by lockstep induction (run-to-run time, not this change). No other result moved: EQ002 13 and
  EQ006 105 on both, and no result went to or from Divergent. So the remaining `CaughtException` alone (25, 3.9%) is
  under ADR 0028's 5%, and nothing is filed: the next use, an argument of a call, is nearly all logging, which this
  ticket and P2-140 leave out of scope. `rethrow` (in 52 changed pairs, alone in 5, in 30 Unknown results) still has
  no owner and stays under 5% alone.
