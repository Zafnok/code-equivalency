# M4-009 `--execute`: every Divergent's counterexample is replayed on both real runtimes
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-032, M3-003, M3-016

## Goal
A Divergent today is a Z3 model that the IR interpreter confirms (ADR 0026). Nothing confirms it
against the code as the CLR runs it. After this ticket:
- `equiv compare --execute` emits each project's compilation;
- for each EQ002, it builds drivers that call the legacy method on .NET Framework 4.8 and the
  modern method on .NET 10 with the model's inputs;
- it records `properties.replay`.

A `reproduced` divergence is one the user can run. A `not-reproduced` one means the model and the
CLR disagree. In a corpus run that is a finding, filed as a soundness or modelling ticket.

## Spec references
ADR 0035 decision 2 and consequences; ADR 0026 (models, taint); VERIFICATION-MODEL.md section 6;
ARCHITECTURE.md (CLI).

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv compare` accepts `--execute`. Without it nothing changes, and no snapshot moves. With
   it, stderr gets one line, `note: --execute runs code from both solutions on this machine`. On a
   non-Windows OS, `--execute` exits 3 with ADR 0035's message.
2. With `--execute`, the frontend emits each loaded project's `Compilation` to a temp directory
   with its references copied next to it. A project whose emit fails makes its pairs'
   `replay` `not-constructible` with reason `emit-failed`, and the run continues.
3. The model's inputs are mapped to C# arguments for parameters of the generator types in M3-032
   (primitives, `char`, `string`, enums, `null`). A static method, or an instance method on a type
   with a public parameterless constructor, is called directly. Any other receiver, any model
   that constrains a heap map, and any other parameter type give `not-constructible` with the
   reason.
4. Replay runs the input once per side under the invariant culture and compares canonical
   outcomes. `properties.replay` is `reproduced`, `not-reproduced` (with both canonical outcomes)
   or `not-constructible` (with the reason).
5. The verdict, rule id, exit code and result fingerprint never change because of replay.
6. `samples/removed-null-check` run with `--execute` on Windows gives `replay: reproduced` on its
   Divergent. A new snapshot, `removed-null-check.execute.sarif`, checks that in.
7. VERIFICATION-MODEL.md section 6 and ARCHITECTURE.md's CLI section are updated for
   `--execute` and `replay`, citing ADR 0035.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/*` (option wiring), `src/Equiv.Frontend.CSharp/Execution/*`
(project emit, method-call drivers), `src/Equiv.Execute/*` (replay entry point),
`src/Equiv.Core/Reporting/SarifReportWriter.cs` (the property), `docs/VERIFICATION-MODEL.md`,
`docs/ARCHITECTURE.md`, tests and one snapshot.

## Tests
`Execute_OffByDefault_NoSnapshotChanges`, `Execute_NonWindows_ExitsThree`, `Execute_PrintsNote`,
`Replay_RemovedNullCheck_Reproduces` (integration, Windows), `Replay_HeapModel_IsNotConstructible`,
`Replay_EmitFailure_IsNotConstructible`, `Replay_NeverChangesVerdictOrFingerprint`.

## Size guard
If you are writing object-graph construction for heap models, stop. That is later work.

## Out of scope
Testing Unknown pairs (P1-007). Heap-carrying models. Multiple cultures on replay. Running any
code without `--execute`.

## Notes
