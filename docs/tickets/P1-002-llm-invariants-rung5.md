# P1-002 Loop ladder rung 5: LLM-proposed coupling invariants, Z3-checked
Status: todo
Effort: M
Depends on: P1-001

## Goal
When rung 4 times out, ask a model for a candidate coupling invariant in SMT-LIB over
the pair's live variables, splice it into the rung-4 obligation, and let Z3 check it.
Accept only on unsat; on sat feed the counterexample back, at most N rounds. A correct
invariant yields unbounded Equivalent with proofMethod llm-invariant; otherwise
Unknown(no-invariant). The model is behind an interface with a deterministic fake for
tests; off by default; enabled by `--invariant-model <name>` and an API key from the
environment. No source code leaves the machine unless this flag is set, and the
CLI says so in its output when it is.

## Spec references
VERIFICATION-MODEL.md section 5.1 rung 5; ADR 0008.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit (accept only on unsat; round limit), property (a random wrong invariant is never accepted), snapshot with the fake model

## Out of scope
Prompt tuning beyond one template. Any model other than the pluggable interface.

## Notes
