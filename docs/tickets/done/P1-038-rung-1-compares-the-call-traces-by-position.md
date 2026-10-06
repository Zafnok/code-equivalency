# P1-038 Rung 1 compares the call traces by position, without sequences
Status: done (PR #414)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-034

## Goal
P1-034 measured that Z3 answers 72 of the 142 rung 1 queries it gives up on for
`gitextensions-8522` once "the traces are equal" is written by position instead of as an equality
of two sequences of a datatype: 26 replay to a Divergent, 42 to Unknown(abstraction), and four
`divergence` queries are proved unsatisfiable (`docs/runs/2026-10-04-trace-encoding.md`). No second
solver and no dependency is involved. When done, rung 1's product compares the traces by position,
and those results are what a run reports.

## Spec references
ADR 0018 (the trace is an observable; unchanged), ADR 0014, ADR 0041 (a closed callee's event has no
heap), VERIFICATION-MODEL.md section 5, `docs/runs/2026-10-04-trace-encoding.md`,
`tools/spikes/trace-encoding/` (`Positional.cs` and the README's argument: the shape, not the code
to keep), `src/Equiv.Verify.Z3/{TraceEncoder,ProductEncoder,FragmentEncoder,LoopLadder}.cs`.

## Design
- `ProductEncoder.Encode` gains a parameter that picks how the two traces are compared: as
  sequences (today's term, the default) or by position. `LoopLadder`'s rung 1 (`Bounded`) passes
  the positional one. Every other caller (rungs 2 to 5, `ContractVerifier`,
  `FailureRefinementQuery`, the contract terms that read `FragmentEncoder.Trace`) is as today.
- The positional comparison is the spike's. Each `FragmentEncoder.CallSite` has its `Reach` and its
  `Position`. The traces are equal when the two lengths are equal (the count at the block the path
  ends in: a return, a throw or an `unreachable`) and, for every old site and new site, both
  reached at equal positions implies the same event: the same canonical callee and the same types
  of arguments and heap read, and equal terms. Two sites of different callees or types give
  `not (both reached at equal positions)`.
- A pair of sites whose positions cannot meet on any path of the two control-flow graphs is left
  out. `FragmentEncoder` exposes what that needs (each site's least and greatest position, each
  side's length term); the spike recomputes both from outside because it could not touch `src/`.
- The exception type is compared as a bv32 in the positional product, so the query holds no
  integer. Under the sequence comparison it stays as it is.
- `TraceEncoder` still builds the `Event` terms: the sequence form remains for the other callers,
  and `ModelDecoder` reads the trace from the replay, not from the model.

Pitfalls.
- The spike measured only queries Z3 gives up on. A query Z3 answers today in the sequence form
  must not become a timeout: criterion 4 is the check, and the size guard says what to do if it
  fails.
- The form is quadratic in call sites. Nothing in the spike decided a query that compared more than
  10,000 pairs of sites, and the largest compared 325,266. Above a cap the product keeps the
  sequence comparison; pick the cap from criterion 4's run and record it.
- The equivalence argument needs each call site to be made at most once on a path. Every product
  `Encode` builds is acyclic, so it holds; say so where the comparison is built, since a loop
  segment's cut events are events too and this ticket still leaves them on the sequence form.
- A site's event shape is what `TraceEncoder.Call` boxes: the arguments, then the heap read unless
  the callee is closed on that side (ADR 0041). Take the shape from one place so the two forms
  cannot drift.

## Acceptance criteria (all must hold; nothing beyond them)
1. Rung 1's product compares the traces by position; its `divergence` query, printed, holds no
   `Seq`, no datatype and no `Int` (unit test on a pair with calls).
2. A property test (CsCheck) over generated acyclic pairs with calls: the `divergence` query has
   the same answer under both comparisons. The spike's nine hand-written pairs are unit tests.
3. Every existing sample and test gives the verdict it gives today.
4. `docs/runs/<date>-positional-trace.md`: a `full` run of `gitextensions-8522` before and after on
   one commit. Per verdict and Unknown reason, the counts; the `timeout` Unknowns that moved, by
   where they went; and every result that was not `timeout` before and is after, which must be none
   or the size guard applies. Apply `equiv-scoreboard`.
5. VERIFICATION-MODEL.md section 5 says how rung 1 compares the traces and why it is the same
   relation. ADR 0018 gets a dated clarification: the trace is the observable it was, and rung 1
   compares it by position.

## Files
`src/Equiv.Verify.Z3/{ProductEncoder,FragmentEncoder,TraceEncoder,LoopLadder}.cs` and one new file
for the comparison, matching tests under `tests/Equiv.Verify.Z3.Tests/`,
`docs/VERIFICATION-MODEL.md`, `docs/adr/0018-*.md` (clarification), `docs/runs/<date>-positional-trace.md`,
`README.md` only through `equiv-scoreboard`.

## Tests
Criteria 1 and 2; a unit test per pitfall that has a case (different types under one callee, a
closed callee, a call under a branch on one side, a pair above the cap keeps the sequence form).

## Size guard
If criterion 4 shows a result that was decided before and is a `timeout` after, do not ship the
positional form as rung 1's only query: ask the sequence form first and the positional one when
Z3 gives up, and record the count in Notes. If the diff changes a rung other than rung 1, stop.

## Out of scope
Rungs 2 to 5 and the contract queries. Bitwuzla, and writing the sorts as bit-vectors for it
(ROADMAP post-MVP). What cvc5 is sent (P1-033 sends what rung 1 asks, whichever form that is).

## Notes
- From P1-034. Bitwuzla 0.9.1 decided 103 of the 142 in the positional form, 46 of them beyond Z3,
  once each uninterpreted sort was written as a 64-bit vector; it proved no pair, so it is not
  adopted here.
- P1-034's read-back could not complete 49 satisfiable answers of the other solvers. Z3's own
  satisfiable answers need no read-back, so that loss does not apply to this ticket.
- Decision: `ProductEncoder.TraceComparison` is an enum beside `Side`, and the cap is a parameter of
  `PositionalTrace.Equal` that defaults to `MaxPairs`, so a unit test reaches it with two calls.
- Decision: the cap counts the pairs of sites that can meet, which is what the spike's 10,000 and
  325,266 counted, not old sites times new sites.
- Decision: rung 1 passes the positional comparison whatever the ladder's `Relation` is. Only
  `ContractVerifier` sets one, and it asks rung 1's query itself through `ProductEncoder.Encode` with the
  default, so it is as before; its ladder runs rungs 2 and 3 only.
- Decision: `FragmentEncoder.CallSite` carries its event's values (`TraceEncoder.Read`, which `Call` boxes
  too) and its least and greatest position; `FragmentEncoder` has `Length` and `ExceptionBits`.
- Decision: criterion 4's "before" is `main` at this branch's base and "after" is the branch, which differ
  by this ticket alone; the product has no switch a run could flip on one commit.
- Reverse postorder visits a branch's `else` target first, so call sites are listed in that order, not in
  block-number order. Nothing depends on it; the pinned test strings show it.
- Criterion 4 (`docs/runs/2026-10-05-positional-trace.md`): rung 1 timeouts 210 to 104, `timeout` Unknowns 148
  to 80, no answered query lost, so the size guard's count is 0 and the cap stays at 10,000.
- Twelve pairs rung 1 answered satisfiable in both runs moved between Divergent and Unknown(`abstraction`),
  six each way: another model of the same query replays differently. Not a timeout, so not the size guard's.
- Decision: the scoreboard is unchanged. Its rows for this pair rest on the pair's latest `full` SUMMARY, and
  the ticket's Files hold a report, not a SUMMARY; `2026-10-05-parallel-verify.md` set the precedent.
- Criterion 3 holds for verdicts, not for the counterexample a Divergent carries: the positional query leads Z3
  to another model. Eight sample snapshots and `removed-null-check.execute.sarif` are regenerated (rule ids as
  before; message, model, fingerprint and two review groups differ), and `SetSshDivergenceTests` no longer
  pins the outcome the solver chose after the traces split. The Z3 test project does not show this; only the
  Windows integration leg does.
