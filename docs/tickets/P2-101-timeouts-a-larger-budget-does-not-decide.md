# P2-101 The timeout Unknowns that 20 times the budget does not decide: find what their queries share
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-050, P2-076

## Goal
P2-050 reran Git Extensions' timeout Unknowns with 4 and 20 times the solver budget
(`docs/runs/2026-10-01-timeout-budget.md`). Of 172 pairs that time out at the default budget, 99
still time out at 20 times it, at 18.7 times the solver time. The 73 that are decided are 20
Divergent and 53 Unknown for another reason (abstraction, unaligned-loop, opaque). None is
Equivalent. Every pair decided on rung 1 was decided by a model, never by an unsatisfiable query. So
the budget is not what stands between these pairs and a proof, and raising the default would buy
run time and no proofs.

79 of the 99 time out on rung 1's first query, on a pair with no loop. Find what those queries have
in common and whether a different tactic pipeline or encoding answers them. This is a measurement
ticket. It changes nothing under `src/`.

## Spec references
VERIFICATION-MODEL.md sections 5 and 6, `src/Equiv.Verify.Z3/Z3Backend.cs` (`Query`, the tactic
pipeline and `Inline`), ticket M3-027 (why the pipeline is what it is),
`docs/runs/2026-10-01-timeout-budget.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. For each of the 99 pairs, record in `docs/runs/<date>-hard-queries.md` (identities and counts
   only): the rung and query that timed out, the size of the assertion set (terms, distinct sorts),
   and which theories it uses (bitvector multiplication or division, arrays for heap maps,
   uninterpreted calls, datatypes for tuples, strings as sorts).
2. Group the pairs by those features and name the three largest groups.
3. For each of the three groups, try on ten pairs at the default `resourceLimit`: Z3's default
   solver, the current pipeline without `Inline`, and one alternative chosen for the group's theory
   (for example bit-blasting tactics for a multiplication group, `qfaufbv` for a heap group). Record
   per alternative how many of the ten are decided, and whether each decided answer is
   unsatisfiable (a proof) or a model.
4. One line in the report: which alternative, if any, proves at least 5 of the 99. If one does, a
   `P2-nnn` ticket to adopt it. If none does, say so; the 99 then stay Unknown until an encoding
   change has its own evidence.

## Files
`docs/runs/<date>-hard-queries.md`, a throwaway harness under `tools/spikes/` or the scratch folder,
new `docs/tickets/P2-nnn-*.md`, `docs/ROADMAP.md`.

## Tests
None. This is a measurement ticket.

## Size guard
If you are editing `src/`, stop; that is the follow-up ticket.

## Out of scope
The nine pairs whose time is outside any budget (P2-076). Run-to-run differences (P2-100). The
Divergents a larger budget finds (their precision is P2-047's question).

## Notes
- From P2-050: at 20 times the budget, 60 pairs are decided on rung 1, all by a model: 36 depend on
  an abstraction, 6 reach an opaque node, 18 replay to a divergence.
