# P1-019 Spike: how many `abstraction` Unknowns does refining the abstraction resolve?
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
`abstraction` is the largest Unknown reason on Git Extensions (261 of 700 in M4-007) and on ServiceAnt
(7 of 18). Such a result has a candidate counterexample that `IrInterpreter` could not replay
untainted, because it ran through an `IrPure` (a shared, uninterpreted operator; ADR 0025) or an
`opaque:` fragment (ADR 0024). Every such Unknown is also `method`-scoped (ADR 0029), so it is
the reason the method-scoped share does not fall. ARDiff-style refinement (post-MVP backlog)
would give the tainting abstraction its real semantics on the candidate's path and query again.
Before anyone designs that, measure how many of these Unknowns it would resolve and in which
direction. Answer with a count, not a design.

## Spec references
ADR 0024, 0025, 0026 (taint, `Abstraction`), ADR 0029 (scope), ROADMAP post-MVP ("ARDiff-style
refinement"), the P1-011 and P1-012 spikes (the shape to follow).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/abstraction-refinement/` reads P2-046's Git Extensions SARIF and, for each
   `abstraction` Unknown, takes its `properties.abstractions`. It groups them by kind: each `IrPure`
   operator, and each `opaque:` reason.
2. For each `IrPure` kind with a closed-form bitvector or boolean meaning (integer arithmetic,
   comparisons, `bool` logic), the tool re-queries the pair with that operator interpreted instead of
   shared, and records one of Equivalent, Divergent (untainted replay), still Unknown, or timeout. An
   `opaque:` kind is only counted, not re-queried.
3. `docs/runs/<date>-abstraction-spike.md` gives a table of kind by outcome, as counts and as
   shares of the `abstraction` Unknowns, plus the ten most frequent kinds. It gives identities and
   kinds only, and no candidate values.
4. If interpreting resolves at least 5% of Git Extensions' Unknowns (ADR 0028's bar, applied as
   P1-011 and P1-012 applied it), write a proposed ADR, because it changes ADR 0025's
   shared-function rule. Otherwise add a measured line to ROADMAP's post-MVP list, as P1-011 did.
5. Nothing under `src/` changes. The spike is not in `Equiv.slnx`.

## Files
`tools/spikes/abstraction-refinement/**`, `docs/runs/<date>-abstraction-spike.md`, and either
`docs/adr/NNNN-*.md` with its `docs/adr/README.md` row, or `docs/ROADMAP.md`.

## Tests
None beyond a `--self-test` over two hand-written pairs: one that resolves to Equivalent when
interpreted, and one that resolves to Divergent.

## Size guard
Interpreting strings, floating point or `decimal` is out of scope (post-MVP theories): count them,
don't encode them. Any edit under `src/`: stop.

## Out of scope
Implementing refinement in the backend. `opaque:` fragments (IL fallback is ADR 0039, P1-014 to P1-018).

## Notes
