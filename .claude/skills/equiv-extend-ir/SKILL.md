---
name: equiv-extend-ir
description: Add or change an IR instruction, type, or lowering rule across Equiv.Core, Equiv.Frontend.CSharp and Equiv.Verify.Z3. Use for any ticket whose goal mentions OperationKind, IR nodes, encoding, or the IOPERATION-COVERAGE table.
---

# Extending the IR end to end

An IR change touches four places, in this order, each green before the next:

1. Spec: add or amend the row in `docs/VERIFICATION-MODEL.md` section 2 (instruction
   table) or section 3 (lowering rules). If semantics are not obvious from the row,
   write one more sentence. This edit ships in the same PR.
2. Core (`Equiv.Core.Ir`): the record, its validator rule, its dump/parse case, its case
   in the test-only interpreter. Tests: unit for the validator; extend the existing
   property generator for dump/parse round-trip (do not write a second generator).
3. Frontend (`Equiv.Frontend.CSharp`): the `OperationKind` case in the lowerer. Tests: a
   minimal C# snippet lowered and snapshot-verified as an IR dump; extend the
   lowering-oracle generator if the construct is expressible in straight-line integer
   code. Update `docs/tickets/IOPERATION-COVERAGE.md` (status, test name, ticket).
4. Backend (`Equiv.Verify.Z3`): the encoding case. Tests: unit (encoder emits the expected
   SMT shape) plus a soundness pair: a program using the construct verified against
   itself (Equivalent) and against a mutated copy (never Equivalent).

Rules:

- One construct per PR. `switch` and `foreach` are two PRs.
- If the construct cannot be encoded exactly, lower it to `IrOpaque` with a reason string
  naming the construct, and add a post-MVP ticket. Never approximate silently.
- Interpreter, dump format and encoder must agree; the property harness in
  `Equiv.Verify.Z3.Tests` (interpreter result equals model evaluation) is the check.
