# P2-067 A lambda or method group converted to a delegate no longer keeps a changed pair opaque
Status: done (PR #327)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-046

## Goal
P2-046's Git Extensions run shows `DelegateCreation` as the single largest reason a changed pair is
opaque: 194 of 1,143 changed pairs (17.0%) carry no other reason, and 328 (28.7%) carry it with
others. ADR 0034 item 2's unlock rule needs an owner for any reason at or above 5%, and this one has
none. M4-004 (done) shares a fragment that is identical on both sides, so an unchanged lambda is
already a shared call; what remains is a pair whose lambda differs, or whose fragment could not be
shared (a capture written later, a runtime-sensitive body). Those keep the whole method opaque.
Decide how a delegate's body is lowered so that a changed lambda contributes a verdict instead of an
opaque, and lower it. Sized as one ticket only after the first criterion's measurement.

## Spec references
ADR 0024 (shared opaque fragments), ADR 0034 (per-ticket unlock rule), ADR 0039 (IL fallback: its
reason list does not include `DelegateCreation`), `docs/tickets/IOPERATION-COVERAGE.md` rows
`DelegateCreation`, `AnonymousFunction`, `TranslatedQuery`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the 194 `DelegateCreation`-only changed pairs by why the fragment is not
   shared, from a `--lower-only` run of the `gitextensions-8522` pair (`equiv-corpus-run`, census
   mode): lambda body differs between the sides, capture written after creation, runtime-sensitive
   body, method group, other. Put the counts in `## Notes`. If one cause is under 20% of them, say
   so and stop; the ticket is then a candidate for closing without code.
2. Route the design through `equiv-adr`'s bar test (a new fragment or inlining scheme is a change
   to ADR 0024's rule for what is shared) and log the outcome as a `Decision:` line.
3. The chosen construct lowers to IR with a test per `IOPERATION-COVERAGE.md` row it changes, and the
   row is updated.
4. On a re-run of the same census, `changedReasonSets["DelegateCreation"]` falls by at least 5% of
   changed pairs (57 pairs) or the ticket records why not. The figure goes in `## Notes`.

## Files
`src/Equiv.Frontend.CSharp/` (the lowering of the chosen construct), `docs/tickets/IOPERATION-COVERAGE.md`,
and an ADR only if criterion 2 says so.

## Tests
Named per changed `IOPERATION-COVERAGE.md` row, in `tests/Equiv.Frontend.CSharp.Tests`.

## Size guard
If criterion 1 shows the lambda bodies differ in ways the solver cannot compare (they call
different members), that is not lowering work: stop and write the finding in `## Notes`.

## Out of scope
The IL fallback (P1-014 to P1-016), `TranslatedQuery` (P2-026), other opaque reasons.

## Notes
- Found by P2-046 (`docs/runs/2026-09-30-full-verdict.md`, per-ticket unlock table).
- **Criterion 1: the split (2026-10-01).** Census (`--lower-only`) of `gitextensions-8522`, legacy
  `3f4ed21998af`, modern `5190ba5c1a5f`, equiv `ef79ff6`, exit 0, 225 s, no project skipped. At this
  commit the census holds 1,294 changed pairs of 13,541 matched, 522 of them without opaque (40.3%), and
  `changedReasonSets["DelegateCreation"]` is 213; 365 changed pairs hold the reason at all. P2-046's
  1,143, 447 and 194 were at `bd8e379`; P1-018's census of 2026-09-30 has this run's three numbers.
  Each `DelegateCreation` fragment of the 213 pairs was classified by a throwaway build that recorded,
  per fragment, which of `FragmentFingerprinter`'s refusals applied or whether the other side holds its
  fingerprint. That build was never committed, and its output stays under `.corpus/`.

  | Why a pair's `DelegateCreation` fragments are not all shared | Pairs | Share of 213 |
  |---|---|---|
  | They are: every fragment's fingerprint is on both sides, and the pair changed somewhere else | 134 | 62.9% |
  | Runtime-sensitive body (the lambda calls a `runtime-changes.json` member, or converts floating point to an integer) | 41 | 19.2% |
  | Capture written after creation, or by a lambda or local function | 15 | 7.0% |
  | Lambda body differs between the sides (fingerprinted, not on the other side) | 12 | 5.6% |
  | Method group (each one of a local function declared outside the conversion) | 5 | 2.3% |
  | Other: a lambda calling a local function declared outside it 4, two of the causes above at once 2 | 6 | 2.8% |

  By fragment, over every changed pair that holds the reason: 764 lambdas and 237 method groups have a
  fingerprint (230 of the method groups are of `this`, 5 static, 2 of a property's value), and 274
  lambdas and 50 method groups have none.
- **What the split says.** The Goal expected the changed lambda and the unshareable fragment. They are
  79 pairs (37.1%), and none of the four named causes reaches 20%. The largest cause was not on the
  list: 134 pairs whose lambdas are unchanged and already shared, counted under the reason only
  because a shared fragment is still an `IrOpaque`. Those pairs are not decided today either: in
  P1-018's full run of 2026-09-30, whose census has this run's numbers, they are 84
  Unknown(abstraction), 32 timeout, 7 unaligned-loop, 5 EQ006 and 6 Equivalent. P1-019's spike found `opaque:DelegateCreation` to be the most frequent
  abstraction kind (93 of 238 results).
- **Size guard, for the 12 pairs whose lambda differs.** In 7 the two sides' lambdas reference
  different members, in 3 the same members, and in 2 only one side has the lambda. So the changed
  lambda is mostly a change the solver cannot compare, and it is not lowered further here: the two
  sides apply two different functions and the pair is Unknown(Abstraction) naming them.
- **Criterion 4: the same census after the change (2026-10-01).** Same pair, same checkouts, this
  branch, exit 0, 232 s, no project skipped. `changedReasonSets["DelegateCreation"]` fell from 213 to
  68: 145 pairs, 11.2% of the 1,294 changed pairs, against a bar of 5% (65 pairs here; the criterion's
  57 is 5% of P2-046's 1,143). Changed pairs holding the reason at all fell from 365 to 138.
  `changedPairsWithoutOpaque` rose from 522 to 667, so the lowerable share (ADR 0034) is 51.5%, up
  from 40.3%. Bodies holding the reason fell from 1,700 legacy and 1,699 modern to 292 and 288.
  Matched, congruent and changed pairs are unchanged (13,541, 12,247 and 1,294), as they must be:
  congruence is decided on the body's fingerprint, which this ticket does not touch. The 82 pairs that
  left a mixed set moved to the set of their other reasons: `switch-pattern` alone is now 109 (was 90),
  `InterpolatedString` 45 (29), `Binary` 45 (42), `Conversion` 29 (22). The 68 pairs left are the 67
  of the split whose fragment has no fingerprint (runtime-sensitive lambdas, captures written later,
  local functions) and one whose shared fragment is a method group with an evaluated receiver. The 145
  that left are 133 of the 134 already-shared pairs and the 12 whose lambda differs.
- **What this does not change.** A pair is lowerable, not decided. Every `IrPure` result is tainted
  (ADR 0026), so the call a delegate is handed to has a tainted event, result and `threw` flag, and the
  branch on that flag taints the rest of the side, exactly where the fragment's own `threw` branch did
  before. The 84 pairs that were Unknown(abstraction) on a shared `DelegateCreation` fragment should
  therefore stay Unknown(abstraction), now naming `delegate:` functions; no full run was made to
  confirm it (a full run of this pair is 2.5 to 9 hours). Deciding them needs ADR 0026 to stop
  tainting an abstraction that both sides apply to equal arguments, which is a backend rule and its
  own ADR question, not lowering. The same goes for the 93 results P1-019 counted under
  `opaque:DelegateCreation`.
- Decision: criterion 1's stop rule ("if one cause is under 20% of them") -> read as "stop when no
  single cause reaches 20%", and continue, because one cause is 62.9%. Alternatives: stop because each
  of the four causes the criterion names is under 20% (runtime-sensitive is 19.2%), which would close
  the ticket with the reason still at 16.5% of changed pairs and no owner. Rule: 3 (the reading that
  criteria 3 and 4 can then pin).
- Decision (criterion 2, `equiv-adr` bar test): a lambda, a static method or a method of `this`
  converted to a delegate is ADR 0025's `IrPure`, named `delegate:<fingerprint>` by ADR 0024's
  fingerprint, over the fragment's reads -> row 1 of the bar test, a dated clarification in ADR 0024
  and in ADR 0025; no new ADR. ADR 0024 rejected "a fragment as a pure uninterpreted function with no
  trace event" because it is "unsound when the fragment contains calls whose order is observable";
  such a conversion evaluates nothing, so the reason does not apply. No Core contract, SARIF shape,
  rule id, verdict meaning or gate changes: `IrPure`, its taint (ADR 0026) and the fingerprint are
  used as accepted. Alternatives: a new ADR; lowering each lambda body as its own matched procedure
  (a new matching rule and a Core contract, and the split shows only 12 pairs would use it); making
  the census skip shared fragments (changes ADR 0034's reason sets, and leaves the conversion a call
  event with a heap effect it does not have). Rule: `equiv-adr` bar test.
- Decision: the function's name -> `delegate:<fingerprint>` for a method group and
  `delegate:<fingerprint>#<n>` for a lambda, `n` its site's position among the body's lambda sites
  with that fingerprint. Alternatives: no site (two lambdas with one body would be one value, but
  they are two methods and never equal delegates, so `e += a; e -= b` would look like
  `e += a; e -= a`: a false Equivalent); the site as an argument (a constant per application, more IR
  for the same thing). Rule: 4.
- Decision: which conversions -> those whose operand is a lambda or anonymous method, a static
  method, or a method of `this`/`base` in a class. A method group with any other receiver stays the
  shared fragment it was, because evaluating the receiver can run code and creating the delegate
  throws when it is null. Alternatives: also lower `o.M` with a null check (the exception type depends
  on whether the method is virtual; 2 fragments on Git Extensions). Rule: 4.
- Decision: a runtime-sensitive lambda stays opaque with no fingerprint -> unchanged from M4-004.
  Alternatives: a side-specific function (`IrPure.RuntimeSensitive`), which would move those 41 pairs
  out of the reason set as well without deciding any of them. Rule: 4.
- Decision: a delegate creation's null shadow is false, pure or opaque -> one more operation in
  `Nullness`'s never-null list, beside `new`. Alternatives: leave it a read of `null.<Sort>`, which
  puts a `NullReferenceException` path, and a branch on a tainted flag, at every `f(x)`. Rule: 1.
- Soundness of the lowering is held by `SharedFragmentTests.AFragmentPairIsNeverEquivalentWhenItsProgramsDiffer`
  (100 generated pairs with a lambda, each run on the CLR on 20 inputs): it passes, and it now counts a
  pair as sharing when both sides apply one `delegate:` function. Once, by hand and not committed,
  M0-012's gate (`DifferentialSoundnessTests`) was pointed at `PairGen.FragmentPair` alone: 200 lambda
  pairs, both lowerings, rules 1 to 3 (soundness, decoding, precision) all hold.
- Checked on three hand-written pairs (not committed): after an unchanged lambda is applied, returning
  `x + 1` on one side and `x + 2` on the other is Unknown(Abstraction), and so is `b + 1` against
  `b + 2`, which never reads the lambda's result; `x + 2` against `2 + x` is Equivalent. That is the
  taint described under "What this does not change".
- A `delegate:` entry of `properties.abstractions` has no `span` and no `reason`, because an `IrPure`
  carries no source span; an `opaque:` entry had both (P2-062). A reader sees a fingerprint and the
  method, not the lambda's line. Giving `IrPure` a span is a Core change (`equiv-extend-ir`), outside
  this ticket's Files. For the 12 pairs whose lambda differs (8 of them Unknown(opaque) in that full
  run) this also means a method-scoped Unknown(Abstraction) where there was an Unknown(Opaque)
  pointing at the lambda.
- Outside the ticket's Files, whatever pins what a lambda lowers to had to follow. In
  `Equiv.Tests.Integration`: `SharedFragmentTests` (the lambda it shares is now a function, and one
  test is new, for a lambda that differs), `IlLoweringParityTests` (the four `business-layer` methods
  with a lambda are now opaque-free under IOperation and stay opaque from IL, so they join its `Known`
  list with that reason), and the `business-layer` census and SARIF snapshots, whose census lines lose
  `DelegateCreation`; no verdict in them changed. In docs: VERIFICATION-MODEL sections 2, 3, 3.1 and
  6 (`equiv-extend-ir` step 1), the sample's README, and the corpus skill's "Top abstractions" line.
  `FragmentLoweringTests` keeps its cases, on fragments that are still opaque (a method group with an
  evaluated receiver, a lifted operator, a query).
- A method group written two ways (`P` and `this.P`, or `new D(P)` and `P`) has two fingerprints,
  because ADR 0024's serialisation keeps each operation's syntax kind. Such a pair is
  Unknown(Abstraction), not Equivalent. Not changed here.
- Interaction with P2-079 (filed on `main` 2026-10-01, read after this work was done). Its repro
  starts from two methods that are Unknown(opaque: `DelegateCreation`) without `--il-fallback`. After
  this ticket a lambda that differs is two `delegate:` functions and no opaque, so those methods
  should be Unknown(Abstraction) and the fallback, which is tried only on an unshared opaque (ADR
  0039), should no longer be tried on them. Not run here. The IL defect is untouched: it still shows
  wherever another unshared opaque triggers the fallback, so P2-079's repro needs one.
