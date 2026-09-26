# M4-009 `--execute`: every Divergent's counterexample is replayed on both real runtimes
Status: done (PR #215)
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
Testing Unknown pairs (P1-008). Heap-carrying models. Multiple cultures on replay. Running any
code without `--execute`.

## Notes
- Decision: every Divergent is replayed, EQ006 included. Criterion text says EQ002, but ADR 0035
  decision 2 says "every Divergent from the solver", and an EQ006 is a Divergent with a model.
- Decision: the frontend hands replay to the CLI as `FrontendAnalysis.Replay`, an
  `IReplayDriverFactory` (Core contract, beside `IExecutionDriverFactory`) over the compilations it
  already loaded. It emits nothing until a replay asks, so a run without `--execute` is unchanged.
  `ReplayPlan`, `ReplayResult` and `ReplayStatus` are the other contract records.
- Decision: `properties.replay` is the status string. A `not-reproduced` result adds
  `properties.replayOutcomes` (`legacy`/`modern`, each `kind` and canonical `value`), and a
  `not-constructible` one adds `properties.replayReason`. The status stays a plain string because ADR
  0035 names the property's three values.
- Decision: the model's inputs are the product's shared inputs, so `ReplayArguments.Bind` rebinds them
  to each side by `ProductEncoder.Pair`'s rule (ADR 0021): C# parameters by position, synthesised
  inputs by name, pairing only equal types. The frontend cannot reference `Equiv.Verify.Z3`, so the
  rule is restated there (about 20 lines). A model whose values do not fit the two parameter lists (a
  loop fragment's) is `not-constructible`.
- Decision: "a model that constrains a heap map" is read as any synthesised input other than `this`
  and `null.*`: `field.*`, `array.*`, `length.*`, `new.*`, `cast.*`, `istype.*`, `typeof.*`. Replay
  builds no object graphs, so none of them can be made to hold the model's value. The `null.*` maps
  are read, since they decide which reference arguments are `null`.
- Decision: a sort element has no content in the model, only identity. A `string` becomes
  `"s<id>"`, and a `float`, `double` or `decimal` becomes the number `id`. Equal elements are then
  equal values and different ones differ. Any other reference type is built only as `null`.
- Decision: the receiver is `new T()` (`DriverSource.Generate(..., constructReceiver: true)`), and
  a struct uses its default. A receiver the model makes null is `not-constructible`. Only public
  methods on public types are called, since the driver is separate source and there is no
  reflection.
- Decision: a divergence whose two model runs end alike (the same `IrOutcome`) is in the call trace,
  because by-ref parameters and heap maps are already `not-constructible`. A driver observes only
  the outcome, so such a replay would report a false `not-reproduced`. It is `not-constructible`
  with that reason.
- Decision: projects are emitted per side into `<temp>/<side>/<assembly>/`, each at most once per
  run, every project as `<assembly>.dll`, which both runtimes probe first. Referenced projects are
  emitted beside it, and referenced files are copied, except from any folder that holds a reference
  assembly (`ReferenceAssemblyAttribute`). Such a folder is a targeting or reference pack. The .NET
  10 reference pack's facades carry no such attribute, and on the first run they were copied next
  to the driver. Each replay's driver is `EquivReplay<n>` in the project's folder, compiled against
  the project's own references. The legacy one is C# 7.3 with an `app.config`, the modern one has a
  `runtimeconfig.json`.
- Decision: the emit reason is `emit-failed: <side> project <assembly>: <errors>`, and a driver that
  does not compile is `the <side> driver does not compile: <errors>`.
- Decision: a side that returns a value with no canonical form, gives no answer, or cannot decode
  its arguments makes the replay `not-constructible` (`the <side> side gave <Kind> <canonical>`).
  None of these is evidence either way.
- Decision: the temporary folder is deleted when the replays finish, as `runtime-diff`'s is. That
  keeps criterion 1's single stderr line. The driver source (`EquivReplay<n>.cs`) and the case
  can be regenerated by rerunning with `--execute`.
- Decision: `Equiv.Cli` references `Equiv.Execute`. That edge was not drawn in ARCHITECTURE.md, so it
  is added there. No architecture rule forbids it.
- Decision: the snapshot is `tests/Equiv.Tests.Integration/removed-null-check.execute.sarif`. It
  differs from `samples/removed-null-check/expected.sarif.json` only by `"replay": "reproduced"`,
  which also shows criterion 5 on a real run.
- Observed: on the sample, the legacy driver on .NET Framework 4.8 gives
  `["Threw","System.ArgumentNullException"]` for `name = null` and the modern driver on .NET 10
  gives `["Threw","System.NullReferenceException"]`. For `"s5"` both return `"Hello, S5"`.
