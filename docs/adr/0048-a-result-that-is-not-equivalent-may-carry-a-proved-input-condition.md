# ADR 0048: A result that is not Equivalent may carry a proved input condition under which the pair agrees

Status: accepted (2026-10-04)

## Context
A Divergent has one counterexample, and an Unknown has a reason. Neither says how much of the input
space is fine. For `samples/removed-null-check` the useful answer is "Equivalent when `name != null`":
the reviewer then asks only whether `null` can arrive. ADR 0029 gave an Unknown a residual claim over
lines (`equivalent unless a relatedLocation is reached`). Nothing gives a claim over inputs, and a
Divergent has no residual claim at all. Verification modulo versions (Logozzo, Lahiri, Fähndrich and
Blackshear, PLDI 2014, doi 10.1145/2594291.2594326) infers such environment conditions from the two
versions of a program. Ticket P1-022.

## Decision
A result that is not Equivalent may carry `properties.agreesWhen`: a predicate over the pair's shared
inputs under which the solver proved the pair Equivalent.

1. **Which results.** A Divergent the solver found that is EQ002, and an Unknown with reason
   `abstraction`. Not EQ006, not an observed Divergent (ADR 0035), no other Unknown, and no pair with
   a loop or a self-call: rung 1's product proves nothing past its bound.
2. **The condition is a hypothesis (ADR 0036).** The proposer harvests the Bool values both bodies
   already compute from shared inputs alone: a source parameter both sides have, such a reference
   parameter's `null.<Sort>` shadow, and constants, combined by `IrBinary` and `IrUnary` only. Each
   value and its negation is a candidate. Nothing is synthesised beyond that.
3. **The checker.** A candidate `p` is admitted when, on rung 1's product, `p` together with "some
   observable differs or a side reaches an unshared opaque node" is unsatisfiable (ADR 0014's second
   query, restricted to `p`), and `p` is satisfiable with the product's own input constraints. The
   first is the proof an Equivalent rests on, for the inputs `p` holds on. The second rules out a
   condition no input meets. A query the solver gives up on admits nothing.
4. **The property.** `agreesWhen` is the disjunction of the admitted candidates:
   `{ smt, text, proposedBy, proofMethod }`. `smt` is SMT-LIB over the product's inputs, `text` the
   same in source spelling with the modern side's parameter names, `proposedBy` is
   `harvested-predicates` and `proofMethod` is `bounded`. The result's message gains the sentence
   `Equivalent when <text>.` A pair with no admitted candidate carries no `agreesWhen` and its
   message is unchanged.
5. **Nothing else depends on it.** The verdict, the rule id, the level, the exit code, the result
   fingerprint, `baselineState`, the review group and the rank are what they were without it.
6. **The claim is as modular as an Equivalent's (ADR 0019).** It assumes the pair's matched callees
   equivalent, so it holds under the same `assumedCallees` and `unprovenAssumptions` the result
   lists.
7. **The counterexample must falsify it.** The result's model satisfies "some observable differs" on
   the same product, so it cannot satisfy an admitted candidate. The backend checks that. A failure
   is a bug in the tool: the result carries no `agreesWhen`, and the run a `warning` notification.

## Why
- It answers the question a reviewer asks next about a Divergent: is this the only way it breaks?
- A wrong candidate costs solver time and can never be reported, because only a proof admits it.
- Harvested predicates are the conditions the code itself distinguishes inputs by. A removed guard
  is the commonest migration regression, and its condition is in the legacy body.
- The proof is ADR 0014's own query. It needs no new encoding and no new soundness argument.

## Rejected
- **A new verdict or rule id ("ConditionallyEquivalent"):** a pair that diverges on some input is
  Divergent. A third state would let a real regression pass a gate that fails on EQ002.
- **Partition verdicts (PASDA; ADR 0037's Rejected list):** those classify every partition of the
  input space, the undecided ones by heuristics. Here no partition gets a verdict. One region is
  claimed, by proof, and nothing is claimed about the rest: not that the pair differs there, and not
  that the condition is the weakest one. ADR 0037's rejection stands.
- **Necessary conditions ("always differs when"):** that is a claim about every input in a region,
  each needing an untainted replay (ADR 0026). It needs its own soundness argument.
- **Synthesising conditions (interpolants, abduction, a model's suggestion):** more conditions for
  more solver time. Harvesting comes first, to measure what it leaves.
- **Reporting an unproved candidate as "likely":** ADR 0036 forbids it.

## Consequences
- VERIFICATION-MODEL.md sections 5 and 6 describe the candidates, the two checks and the property.
- A queried pair costs one more encoding and up to two queries per candidate, at most 16 candidates,
  each with the pair's resource limit and timeout. The census reports the pairs queried, the pairs
  with a condition and the time spent.
- The SARIF gains a property and a message sentence, so the next release is a minor one.
- Conditions over the heap, over a callee's result, or for looping pairs are later tickets. So is
  counting the inputs a condition covers (P1-027).
