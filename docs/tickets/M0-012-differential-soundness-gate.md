# M0-012 Differential soundness gate: generated C# pairs, executed on the CLR, never contradict the verdict
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-002, M3-007, P1-006

## Goal
Today no gate can see a false Equivalent that enters between C# and the verdict. Section 7's
soundness harness generates IR, so it cannot see a lowering gap. The lowering oracle runs only
straight-line integer methods and checks IR against C#, not verdicts. Seeded recall (ADR 0028)
waits for M4-007. This ticket adds a property-test gate that closes the loop end to end on
generated code:
- generate a C# method, and a second one derived from it by a mutation operator;
- compile and run both with generated inputs;
- run the real frontend and the Z3 backend on the pair;
- assert that the verdict never contradicts what execution observed.

It is the metamorphic-testing idea from EMI (Le, Afshari and Su, PLDI 2014), pointed at the
checker instead of a compiler. It runs on every PR with a small budget and nightly with a large
one. The mutation operators it defines are reused by M4-010 on the corpus.

## Spec references
VERIFICATION-MODEL.md sections 1, 6 and 7; ADR 0014; ADR 0026; ADR 0028 (seeded recall); ADR
0007 (test stack).

## Design
- Generator: extend `LoweringOracleGen` into `PairGen` over the constructs `IOPERATION-COVERAGE.md`
  marks lowered: integer and bool arithmetic, `if`/`else`, `switch` on integers, `while` and `for`
  with small bounds, `throw`, null checks on a `string` parameter, one field, one array parameter.
  Methods stay under 40 statements.
- Two operator families, each a closed enum:
  - `Preserving`: rename locals, reorder independent statements, invert an `if` and swap its
    branches, `x + y` to `y + x`, introduce and inline a temporary.
  - `Changing`: flip a comparison, change a constant by one, drop a null check, drop a field
    write, swap two arguments, and move a `throw` across a side effect.

  `Changing` operators can produce equivalent mutants. The test never assumes a mutant differs: it
  asserts only against what execution observes.
- Execution: in-process, exactly as `LoweringOracleTests` does it (Roslyn `Emit` into a
  collectible `AssemblyLoadContext`, invoke). This is a test project, so the lowering oracle's
  precedent applies. Inputs come from CsCheck plus every model the backend returned.
- Oracle rules, over each generated pair and its inputs:
  1. If any input gives different observables (return value, exception type, field and array
     state), the verdict is not Equivalent.
  2. If the verdict is Divergent (EQ002), replaying its model in C# gives different observables.
  3. If the operator is `Preserving`, the verdict is not Divergent.

  Rule 1 is the soundness rule. Rules 2 and 3 are the precision and decoding rules.
- The shrunk failing pair is written to the test output as two C# methods and a model, so a
  failure is a ready-made regression test.

## Acceptance criteria (all must hold; nothing beyond them)
1. `PairGen` in `tests/Equiv.TestSupport` generates `(string LegacySource, string ModernSource,
   MutationOperator Operator)` over the construct set in Design. `MutationOperator` is an enum
   with every operator listed in Design.
2. `DifferentialSoundnessTests` in `tests/Equiv.Tests.Integration` runs the real
   `Equiv.Frontend.CSharp` lowering and the real `Z3Backend` on each pair and asserts oracle
   rules 1 to 3.
3. The per-PR budget is 200 pairs. The nightly `mutation.yml` schedule (or a new nightly job in
   `ci.yml`, whichever already has a schedule trigger) runs 5,000. Both budgets and the seed
   come from one constant, and the seed can be overridden with an environment variable.
4. A failure prints both methods, the operator, the input and both observables, with no other
   output.
5. Each operator family is exercised: `PairGenTests.EveryOperatorIsDrawn` asserts that every
   enum member appears within the first 2,000 draws at the fixed seed.
6. VERIFICATION-MODEL.md section 7 gains the obligation "Differential soundness (property test,
   M0-012)", stating the three rules. QUALITY-GATES.md lists the test under the blocking unit and
   property gate.
7. Any rule 1 failure found while writing this ticket is not fixed here. It gets a
   `P2-nnn-soundness-<slug>.md` ticket, and the case is added to a skip list that names that
   ticket. The skip list must be empty before M3-003 lands.

## Files
`tests/Equiv.TestSupport/PairGen.cs`, `tests/Equiv.TestSupport/MutationOperator.cs`,
`tests/Equiv.TestSupport/Mutations/*.cs`, `tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`,
`tests/Equiv.Tests.Integration/PairGenTests.cs`,
`.github/workflows/*.yml` (the nightly budget only), `docs/VERIFICATION-MODEL.md` (section 7),
`docs/QUALITY-GATES.md`.

## Tests
`PairGenTests.EveryOperatorIsDrawn`, `PairGenTests.PreservingOperatorsCompile`,
`PairGenTests.ChangingOperatorsCompile`,
`DifferentialSoundnessTests.ObservedDivergenceIsNeverEquivalent`,
`DifferentialSoundnessTests.DivergentModelReplaysAsDivergence`,
`DifferentialSoundnessTests.PreservingMutationIsNeverDivergent`.

## Size guard
More than 12 new files, or any change under `src/`, means you are fixing what the gate found.
Stop and file the P2 ticket instead (criterion 7).

## Out of scope
Running on .NET Framework 4.8 (that is ADR 0035's harness). Corpus code (M4-010). New lowering.
Coverage-guided generation.

## Notes
- Decision: `SwapArguments` swaps the operands of a binary operator or comparison of one operand type (not a
  shift). The construct set has no calls, so operands are the only arguments there are.
- Decision: `InlineTemporary` is `IntroduceTemporary` with the sides swapped: the legacy method has the temporary.
  Every operator is then its own enum member and is drawn on its own.
- Decision: `ReorderIndependentStatements` and `Commute` act only where nothing involved can throw (no division,
  no checked operation or conversion, no element read), since swapping two throwing operands changes which
  exception wins; `MoveThrowAcrossSideEffect` swaps an `if` that throws with an adjacent write of `F` or `u[i]`.
- Decision: each side is its own compilation of `class Oracle`, so both sides' static field is the one
  `field.Oracle.F` input; both images load into one collectible `AssemblyLoadContext` per check.
- Decision: a pair is lowered and verified once, memoised by its two sources (`PairRuntime.Analyse`), and the
  three rule tests share it; they draw the same pairs because they share the seed.
- Decision: a Divergent model is replayed from the shared inputs by name (legacy parameters, then the modern ones
  the legacy side lacks, as `ProductEncoder.Pair` orders them). An array longer than three replays as three
  elements, since no generated method reads index 3 or the length; a negative length is reported as a rule 2
  failure, not replayed.
- Decision: the nightly budget is a new `differential` job in `mutation.yml` (the workflow with the schedule),
  on `windows-latest` like the per-PR run, gated on `github.event_name == 'schedule'`.
- Deviation: two files beyond the Files list, `tests/Equiv.TestSupport/PairInput.cs` (the analyzers want one
  top-level type per file) and `tests/Equiv.Tests.Integration/PairRuntime.cs` (compile, run and verify, shared
  by `DifferentialSoundnessTests` and `PairGenTests`). Nine new files in all; nothing under `src/`.
- Found: at 200 pairs every rule held (39 Divergent, 154 Equivalent, 7 Unknown, spread over both
  families). At 5,000, rules 1 and 3 held and rule 2 failed once: a Divergent model gives `u` the length
  `-2147483648`, because nothing constrains `length.<Sort>` to be non-negative. That is a false-Divergent
  (precision) bug, not a rule 1 failure; it is filed as P2-019 and skipped by that symptom. With the
  skip, 5,000 pairs pass all three rules (about 4 minutes locally). M3-003 now depends on P2-019.
- Decision: the skip list matches a symptom in the failure's input line, not a pair. An exact-source
  entry would skip only the pair CsCheck shrank to, and the nightly would still draw the unshrunk one.
- Note: CsCheck puts one line before the printed failure, with the seed that replays it. The rest of
  the message is what criterion 4 lists and nothing else. CsCheck shrinks these pairs poorly (5 shrinks
  on the P2-019 case), because shrinking a generated method rarely keeps the mutation site.
