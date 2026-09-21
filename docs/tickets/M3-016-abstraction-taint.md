# M3-016 Replay taint: a Divergent that depends on an abstraction is Unknown(Abstraction)
Status: todo (blocked on ADR 0026 acceptance)
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-001

## Goal
ADR 0026: keep EQ002 exact once shared fragments (M3-017) and pure operators (M3-018) let the solver
interpret code freely. The replay tracks taint, and only an untainted divergence is Divergent.
This ticket lands the mechanism before any abstraction exists. It is exercised through
call identities with the `opaque:` prefix, which the lowerer does not emit yet.

## Spec references
ADR 0026; ADR 0018; ADR 0019; VERIFICATION-MODEL sections 5 and 6; M3-001 replay design.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrInterpreter` gains an optional taint predicate over call identities, and it treats `IrPure`
   the same way once that instruction exists. A tainted run marks:
   - the results and `threw` flags of tainted calls;
   - anything computed from a tainted value, through every instruction and phi;
   - after a branch or switch on a tainted condition, every later definition, trace event, `outs`
     value and the outcome of that side.
2. `IrRun` exposes, per observable, whether it is tainted. The existing untainted API and its
   results do not change.
3. `ModelDecoder.Replay` returns Divergent only when some compared observable differs and that
   observable is untainted on both sides. For the trace, it compares the first differing event.
   A tainted-only difference yields `Unknown(UnknownReason.Abstraction, detail)`. The detail names
   the tainting identities. The decoded model travels as a candidate counterexample.
4. `UnknownReason.Abstraction` exists, and SARIF writes `properties.candidateCounterexample` in the
   `CounterexampleText` rendering and `properties.abstractions` (identities and spans) for it.
5. An untainted replay whose compared observables agree is still the M3-001 encoder-bug failure.
6. Tests build IR directly: a divergence only through an `opaque:` call is Unknown(Abstraction); a
   divergence on an untainted return in a procedure that also calls `opaque:` is Divergent; a
   branch on an `opaque:` result taints the rest of the path.

## Files
`src/Equiv.Core/Ir/IrInterpreter.cs`, `src/Equiv.Core/Ir/IrRun*.cs`,
`src/Equiv.Core/Verdicts/UnknownReason.cs`, `src/Equiv.Verify.Z3/ModelDecoder.cs`,
`src/Equiv.Core/Reporting/*`, tests in the matching projects.

## Tests
`TaintFlowsThroughDataDependencies`, `BranchOnTaintTaintsTheRestOfTheSide`,
`UntaintedDivergenceIsDivergent`, `TaintOnlyDivergenceIsUnknownAbstraction`,
`CandidateCounterexampleIsWritten`, `UntaintedAgreementIsStillAnEncoderBug`, plus a CsCheck property:
with no taint predicate, results equal the M3-001 interpreter's.

## Size guard
No change to `ProductEncoder`. If one appears necessary, stop: taint is a replay concern.

## Out of scope
Emitting abstractions (M3-017, M3-018). Locations (M3-023).

## Notes
