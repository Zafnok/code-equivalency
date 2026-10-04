# P1-027 Spike: on what share of inputs does a Divergent pair diverge?
Status: done (PR #389)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
A Divergent gives one input. Whether the two sides disagree on that input alone or on half of all
inputs is not reported, and it decides how a reviewer triages. `--execute` estimates a likelihood by
sampling (P1-008), and only for Unknowns. Approximate model counting computes the measure from the
formula: ApproxMC (Chakraborty, Meel and Vardi; citation from memory, check before relying on it)
counts the models of a formula to within a stated factor using a satisfiability solver and random
parity constraints. It needs no assumption beyond the solver.

Measure whether this is computable on this tool's Divergents, and whether the number says anything.
Answer with counts, not a design.

## Spec references
ADR 0026 (a Divergent's model is untainted), ADR 0021 (shared inputs), ADR 0035 (a likelihood is
stated for a distribution), VERIFICATION-MODEL.md sections 2 and 5 (inputs, the product),
`docs/runs/2026-09-30-divergent-audit.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/divergent-count/` reads the EQ002 results of the three large runs and classifies
   each pair's shared inputs: countable (every source parameter is `Bool`, a bitvector or a
   reference used only through its null shadow) or not, with the first input that makes it not (a
   heap map the divergence reads, an uninterpreted sort compared by value, a call result).
2. For each countable pair it counts the assignments of the source parameters for which some
   assignment of the rest makes an observable differ, by the hashing scheme (random parity
   constraints over the parameter bits, on Z3) with tolerance 0.8 and confidence 0.8, inside the
   pair's `timeoutMs`. It records the count as a share of the parameter space, or timeout.
3. On ten hand-written pairs whose share is known exactly (in the spike's self-test), every estimate
   is within the tolerance.
4. `docs/runs/<date>-divergent-count-spike.md` reports the countable share of EQ002 results, the
   share counted in time, the distribution of the shares in decades (`1`, `2^-1..2^-8`, ..., a single
   input), and for the audited results of P2-047 whether confirmed and false-positive results differ
   in share. Identities and counts only.
5. If at least half the EQ002 results are counted in time, write a proposed ADR (a
   `properties.divergentShare` with its tolerance, confidence and the input space it is over) and its
   ticket. Otherwise add one measured line to ROADMAP's post-MVP list.
6. Nothing under `src/` changes. No new package.

## Files
`tools/spikes/divergent-count/**`, `docs/runs/<date>-divergent-count-spike.md`, and either
`docs/adr/NNNN-*.md` with its README row and a ticket, or `docs/ROADMAP.md`.

## Tests
The self-test of criterion 3.

## Size guard
An external model counter, counting over heap maps, or weighting inputs by a distribution: stop.
Any edit under `src/`: stop.

## Out of scope
Naming the inputs that diverge (`name == null`): that is P1-022's `agreesWhen`, negated. Counting on
Unknown results.

## Notes
- From the 2026-10-03 improvement review ("Counting divergent inputs").
- A share is over the parameter bit space, uniformly. It is not a probability that production
  traffic diverges; the report and any later property must say so.
- Result: 12 of 80 EQ002 results counted in time (15.0%), all with share 1, 11 with no source
  parameter. Below half, so ROADMAP's post-MVP line and no ADR
  (`docs/runs/2026-10-04-divergent-count-spike.md`).
- Decision: the ticket's three blockers are read as uses of a `Sort` source parameter: an argument
  of a call (`call-result`), a key of a field, array or length map (`heap-map`), any other read of
  its value (`sort-by-value`). Heap maps and call results that no source parameter reaches are
  existential and block nothing, as criterion 2 says.
- Decision: 17 iterations, the least odd count whose majority is within tolerance with probability
  0.8 when one iteration is with probability 0.6. The 0.6 is the paper's bound from memory; the PDF
  could not be read here. The threshold formula was checked against the reference implementation.
- Decision: parity rows go to Z3 in reduced echelon form, and the count uses the incremental solver
  with the verdict's tactic pipeline as the fallback. Without the first, every 32-bit self-test pair
  gave up; without the second, two zero-parameter corpus pairs did.
- Decision: the `gitextensions-8522` SARIF read is `20261002-1839-full-after-again` (68 EQ002), the
  latest plain full run on the box; the 2026-09-29 run of the audit (84 EQ002) is no longer on disk.
  The audit's rows are measured by identity.
- Surprise: this worktree has no `.corpus/`. The runs and checkouts were read in place from the
  `sad-maxwell-71be44` and `version-upgrade-pairs-corpus-1c9943` worktrees, with
  `TargetFrameworkRootPath` pointed at that worktree's `refasm`.
- Limit: a divergence that relates two parameters (`a == b`) is a dense GF(2) system under parity
  rows and Z3 gives up on it. The self-test prints it as an eleventh, non-failing pair.
