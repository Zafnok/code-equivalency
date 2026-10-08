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
- Decision: which pairs are the 99 -> the identities in the output P2-050's harness left on this box
  (its `budget20x` run: Unknown(timeout) at 5,000 ms and at 100,000 ms, of the 184), which split 79
  and 20 as the report says. Alternatives: finding them again with a run at 20 times the budget
  (hours, and `main` has moved, so it would find other pairs). Rule: 4.
- Decision: the default `resourceLimit` -> 2,000,000, today's `EquivConfig.DefaultResourceLimit`
  (ADR 0049), not the 5,000,000 P2-050 chose. Six solvers were also asked at 30,000,000, the budget
  pass's limit. Alternatives: 5,000,000 throughout. Rule: 1.
- Decision: how the queries were captured -> a throwaway harness (`tools/spikes/hard-queries/`)
  that loads the pair once through the production frontend, asks each of the 99 what the ladder
  asks, and writes the first query of each rung Z3 gives up on as SMT-LIB text, so that every later
  check needs neither the pair nor a load. No full run was needed: loading took 127 s and the 99
  pairs under four minutes. Alternatives: a hook in `Z3Backend.Query` (edits `src/`), a full
  `compare` run with a debug log (hours). Rule: 4.
- Decision: the query recorded for a looping pair -> rung 2's first obligation that times out, since
  rung 1 proves a looping pair only when no input goes past the bound; rung 1's is exported beside
  it. Alternatives: rung 1's query for every pair. Rule: 1.
- Decision: the groups -> by the set of theories the query uses, and the three sets that hold the
  trace's sequences (three pairs each) are one group, the nine looping pairs. Alternatives: the six
  exact sets, which leaves a three-way tie for third. Rule: 4.
- Decision: every member of each group was tried (26, 15 and 9), not ten -> a check takes seconds at
  this limit and criterion 4 counts over all the pairs anyway; the first ten of each group are a
  column of the table. Rule: 3.
- Decision: the alternative chosen for each group -> `qfaufbv` for heap maps and calls (the ticket's
  own example); `sat` with `euf=true`, Z3's newer core built around uninterpreted functions, for the
  group with pure functions; and for the sequence trace the obligation encoded with its traces
  compared by position (`ProductEncoder.TraceComparison.Positional`, P1-038's encoding, which rung 2
  does not use), with the production solver. Alternatives: `qfufbv` (rejects arrays), bit-blasting
  (only three queries hold a multiplication). Rule: 1.
- Decision: a satisfiable answer counts as a model only when every assertion as written is true in
  it -> `qfaufbv` returned one that is not, and `sat` with `euf=true` is a core Z3 calls
  preliminary. Rule: 3.
- Observed: 48 of the 99 are no longer timeouts on `main` (`c59fa0fc`) at the default budget: 34
  Equivalent, 6 Unknown(opaque), 4 Divergent, 4 Unknown(abstraction). P2-050 ran `ef79ff6`; P1-038,
  P1-031 and P1-030 landed since. The ticket's 99 are 51 today.
- Observed: all 51 queries hold heap maps and uninterpreted calls (a median 107 array reads and 814
  call applications over 12,710 distinct terms and 43 sorts); 3 hold a multiplication of two
  non-constants. The pipeline's preprocessing spends a median 132,000 of the 2,000,000 units.
- Observed: `smt` with no tactic before it is not bounded by `rlimit`: 15 of 51 checks spent over
  10% more than the limit and one spent 31,700,000 against 2,000,000. Its models are therefore a
  larger budget's. The production pipeline kept to the limit on every check.
- Observed: the positional trace does not help rung 2: of nine obligations eight still time out and
  one becomes satisfiable, and one of the three looping pairs rung 2 proves today times out with it.
- Result: `docs/runs/2026-10-07-hard-queries.md`. No alternative proves at least 5 of the 99; none
  proves one. No follow-up ticket (criterion 4).
