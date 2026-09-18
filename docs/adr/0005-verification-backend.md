# ADR 0005: Direct Z3 encoding; Lean and Boogie rejected for the MVP

Status: accepted (2026-09-17)

## Decision
`Equiv.Verify.Z3` encodes a bounded product program straight into Z3 via `Microsoft.Z3`.

## Why
- The question (two procedures, same inputs, can the outputs differ) is exactly what
  bounded model checking over SMT answers. SymDiff (Microsoft Research) proved the
  product-program approach on C#/C at scale.
- Bitvectors give exact C# integer semantics (overflow, `checked`), SMT arrays model the
  heap, uninterpreted functions model calls we do not inline.

## Rejected
- Lean: an interactive prover. Exporting gives you a goal, not a proof; someone (or an
  LLM, slowly and unreliably) still writes the proof. Z3 is push-button. Lean is the
  right tool for verifying our own core algorithms someday, not user code. Overkill,
  as suspected.
- Boogie (SymDiff's IVL): its real advantage is loop invariants and unbounded proofs.
  Cost is a second IR and a thinly documented .NET package. Deferred behind
  `IVerificationBackend`; adopt when bounded verdicts prove insufficient in practice.
- Symbolic execution (KLEE-style): nothing production-grade for .NET; Pex/IntelliTest is dead.

## Consequences
Verdicts on loops are bounded and labelled so. Unknown is an honest, expected outcome.
