# ADR 0026: A Divergent must not depend on an abstraction; otherwise it is Unknown(Abstraction)

Status: proposed (2026-09-21)

## Context
M3-001 reports Divergent only for a counterexample "whose replay reaches no `IrOpaque`, so replay is
exact". A replay that fails to diverge is treated as an encoder bug. ADRs 0024 and 0025 add
abstractions that the solver can interpret any way it likes: shared opaque fragments and
`IrPure` functions. With those in place, the solver can make up a divergence that no real run
produces. Take old `a + b` against new `b + a` on `double`: `f64.add(a, b) != f64.add(b, a)`
satisfies the solver. Reporting that as EQ002 fails a PR gate on a false alarm, and a gate that
cries wolf gets switched off. Treating every counterexample that touches an abstraction as Unknown
would instead waste a real divergence found on another path.

## Decision
`IrInterpreter` replays a counterexample with **taint tracking**. Tainted values are:
- a result or `threw` flag produced by `IrPure`;
- a result or `threw` flag produced by a call whose identity is `opaque:<fingerprint>`;
- anything computed from a tainted value;
- every definition, trace event, `outs` value and outcome after a branch or switch on a tainted
  condition. The rest of that side is tainted.

The result is **Divergent** only when some observable the encoder compares differs, and the value
on both sides is untainted. For the call trace that means the first differing event. Otherwise it is
**Unknown** with a new `UnknownReason.Abstraction`. The SARIF result then carries the model as
`properties.candidateCounterexample`, and the abstractions it depends on as
`properties.abstractions`, with their functions or fingerprints and source spans. A replay whose
untainted observables agree while the solver said they differ remains an encoder bug (M3-001).

## Why
- Ordinary `IrCall` results stay untainted. Their arbitrariness is the accepted modular assumption
  (ADRs 0018 and 0019): with position keying, a divergence can only depend on a call result
  after the traces already differ, and trace differences are real observables.
- A divergence on an untainted value does not depend on how the solver chose the abstractions,
  so any real interpretation of them reproduces it. That keeps EQ002 exact.
- A candidate counterexample is still useful to a reviewer. Attaching it turns "Unknown" into
  "this input probably diverges; confirm by hand", which is much cheaper to review.
- Control taint ends the tainted side's comparison at the first abstract branch. That is the
  simplest rule that stays sound when later trace positions shift.

## Rejected
- **Any counterexample touching an abstraction is Unknown.** Sound, but it throws away real
  divergences found on untainted paths of mixed procedures.
- **Asking the solver for a second model that avoids the abstraction.** It costs another call per
  result and still does not show that the divergence is realisable.
- **Reporting tainted divergences as EQ002 with a "may be spurious" note.** It still fails the
  gate, and people stop reading notes.

## Consequences
- EQ002 stays a claim the tool stands behind. Unknown gains the reason Abstraction, which by
  construction comes with a candidate input.
- `IrInterpreter` grows a taint shadow. Its oracles and budget are unchanged.
- VERIFICATION-MODEL section 6 (the Unknown row, the candidate counterexample) and the M3-001 replay
  paragraph change when this ADR is accepted.
- Ticket: M3-016. M3-017 and M3-018 depend on it.
