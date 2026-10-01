# P2-055 Runtime rules apply only across the runtimes a pair crosses
Status: done (PR #324)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-053, P2-054

## Goal
Switch every consumer of runtime sensitivity from "always" to "if the pair's interval crosses it"
(ADR 0040 decision 2). After this, a same-runtime pair has no runtime-changed callee, no EQ006, and
no body made non-congruent by a runtime rule. A net8-to-net10 pair gets only the rows changed in
net9 or net10. A 4.8-to-10 pair keeps every verdict it has today.

## Spec references
ADR 0040; ADR 0024 (runtime-sensitive bodies are never congruent); ADR 0025 (side-specific pure
functions); VERIFICATION-MODEL.md sections on runtime sensitivity and EQ006.

## Design
Lowering runs per side, before procedure pairing, but an interval needs both sides. Compute it per
project before lowering:
- a project's counterpart is the project on the other side with the same assembly name;
- the interval runs from the legacy project's runtime to the modern project's runtime;
- a project with no counterpart takes the widest interval between its runtime and any runtime on
  the other side;
- for an `unhosted` project, apply ADR 0040's rule: an equal `netstandard` pair is the same runtime,
  and anything else crosses the table's whole coverage.

Record the choice as a `Decision:` line if it differs. Each consumer then receives the project's
`RuntimeInterval` in place of the table alone:
- `Lowering/CallIdentityFactory.cs:57` (`RuntimeChanged`), which feeds `TraceEncoder` and `VerdictRule`;
- `Fingerprinting/BoundSerialiser.cs`:
  - line 154, float-to-integer, which becomes the built-in rule `changedIn: net9.0`;
  - line 72, x87;
  - lines 260 and 268, runtime-changed members;
- `PureCatalogue.RuntimeSensitive` and `IrLowerer.cs:98`: x87 applies only when exactly one side of
  the interval is a .NET Framework runtime on the 32-bit JIT. A .NET (Core) project is never x87.

Pitfall: a fragment shared between sides (ADR 0024) must be computed with the same interval on both
sides, or it will not share.

## Acceptance criteria (all must hold; nothing beyond them)
1. Every consumer listed in Design takes the project's `RuntimeInterval`, and
   `RuntimeChangeTable.TryMatch`'s interval-free overload is removed.
2. New sample `samples/same-runtime-cleanup/`, net10.0 on both sides:
   - a byte-identical method calling `double.ToString()`, `string.StartsWith(string)` and a
     float-to-int cast is Equivalent by congruence (today it is EQ006);
   - a cleaned-up method (an `if` chain made a `switch` expression) is Equivalent.
3. New sample `samples/version-bump/`, net8.0 against net10.0:
   - a byte-identical method calling a member whose row has `changedIn: net9.0` or later is not
     congruent, and reports EQ006 when the solver finds the difference;
   - a byte-identical method calling a member whose row has `changedIn: netcoreapp3.0` is
     Equivalent by congruence.
4. Every existing sample's `expected.sarif.json` is unchanged except for the EQ006 rule text that
   criterion 6 rewords (all current samples are 4.8 against 10, and none has an EQ006 result).
5. A pair whose interval reaches below `coveredFrom` produces one run-level
   `toolExecutionNotification` naming the uncovered range. The unit test uses a netcoreapp2.1
   fixture.
6. The EQ006 rule text in `SarifReportWriter` says "differs between the two sides' runtimes", and
   the message names both runtimes.
7. VERIFICATION-MODEL.md's runtime-sensitivity and EQ006 text say that a rule applies inside the
   pair's interval.

## Files
`src/Equiv.Frontend.CSharp/Lowering/CallIdentityFactory.cs`, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, `src/Equiv.Frontend.CSharp/Fingerprinting/FragmentFingerprinter.cs`,
`src/Equiv.Frontend.CSharp/Lowering/PureCatalogue.cs`, `src/Equiv.Frontend.CSharp/CSharpFrontend.cs`,
`src/Equiv.Core/RuntimeChanges/RuntimeChangeTable.cs`, `src/Equiv.Core/Reporting/SarifReportWriter.cs`,
`samples/same-runtime-cleanup/**`, `samples/version-bump/**`, `docs/VERIFICATION-MODEL.md`, tests, snapshots.

## Tests
`CallIdentityFactoryTests.SameRuntime_NothingIsRuntimeChanged`,
`CallIdentityFactoryTests.RowOutsideTheInterval_DoesNotApply`,
`BoundSerialiserTests.FloatToIntIsSensitiveOnlyAcrossNet9`,
`BoundSerialiserTests.X87OnlyWhenExactlyOneSideIsFramework32Bit`,
`FragmentFingerprinterTests.SharedFragmentUsesOneInterval`,
`SamplesEndToEndTests` rows for both new samples,
`CompareCommandTests.UncoveredRuntimeRangeIsNotified`.

## Size guard
A change to any verdict on an existing 4.8-to-10 sample means the interval is wrong: stop. A new
rule id or Unknown reason: stop (ADR).

## Out of scope
Execution (P2-056). `api-equivalences.json` (ADR 0040 leaves it as is).

## Notes
- Deviation: criterion 4 said the existing snapshots change only in `run.properties.runtimes`. P2-053 had already added that property, so it does not change here; what changes is line 61 of every snapshot, the EQ006 rule description that criterion 6 rewords. No existing sample has an EQ006 result, so no message changed and no verdict, fingerprint or exit code did. Criterion 4's text is corrected above.
- Decision: where the interval is computed -> per matched pair, from the two projects that hold the pair's bodies. Alternatives: per project by counterpart assembly name, as Design describes. Rule: 4. Design assumed lowering runs per side before pairing, but `CSharpFrontend.Lowered` lowers pair by pair, so both projects are known. This needs no "widest interval" fallback for a renamed assembly, and both bodies get the same interval by construction, which is what the pitfall asks for.
- Decision: a project hosted on several runtimes -> the interval runs from the oldest to the newest runtime of either project. Alternatives: the first runtime, as replay uses; one interval per host. Rule: 4 (the widest interval only adds rules, so it is the sound choice).
- Decision: how consumers receive it -> `Lowering/SideRuntime(RuntimeInterval Interval, bool X87)`, built once per pair by `SideRuntime.Of`. `CallIdentityFactory` and `IlFragment` take the `RuntimeInterval`; `BoundSerialiser.Settings`, `FragmentFingerprinter`, `BodyFingerprinter`, `PureCatalogue.Entry.RuntimeSensitive`, `IrLowerer` and `IlLowerer` take the `SideRuntime`, replacing their `legacy`/`x87` flag. Alternatives: two parameters everywhere; computing x87 inside each consumer. Rule: 2. x87 needs the other side's project, which a consumer does not have.
- Decision: x87 -> a side is flagged when its project may run on the 32-bit .NET Framework JIT (32-bit platform, and a .NET Framework runtime or none known) and the other side's does not certainly (32-bit platform and every runtime .NET Framework). It does not depend on the interval being non-empty: net48 x86 against net48 x64 is one runtime and still differs in floating point. Alternatives: only when the interval crosses the Framework boundary. Rule: 3. For a 4.8 x86 to .NET 10 pair this is the legacy side only, as before.
- Decision: a pair whose runtimes are not known -> `RuntimeChangeTable.Coverage`, the interval from .NET Framework 4.0 to the newest `changedIn`, which crosses every row and `net9.0`. It is used for an unhosted pair (ADR 0040 decision 1), a result with no runtimes, and a census pair with none. Alternatives: a flag on `RuntimeInterval`; a nullable interval in every consumer. Rule: 4. Two unhosted projects on the same `netstandard` get the empty interval at `coveredFrom`.
- Decision: how the interval reaches the report -> `ProcedurePair.Runtimes`, copied to `VerificationResult.Runtimes` beside `Lowering`; `VerdictRule.Describe` takes it and looks the EQ006 row up inside it. Alternatives: a per-run interval on `FrontendAnalysis`; the row on `CallIdentity`. Rule: 1. Criteria 5 and 6 need it in Core and the CLI, per pair. P2-053's note that only the frontend consumes the runtime no longer holds.
- Decision: EQ006 message -> `<identity> diverges via a runtime-changed API between <older> and <newer> (<reason> <url>): <model>`, the interval's ends with the older first, whichever side it is. Alternatives: `legacy <runtime>, modern <runtime>` from `run.properties.runtimes`' strings. Rule: 4.
- Decision: the uncovered-range notification -> one `warning` per run naming the widest uncovered range (oldest start, newest end) and how many matched pairs cross it, on stderr too, with `descriptor.id` `uncovered-runtime-range`. It does not change the exit code or `executionSuccessful`. Alternatives: one notification per distinct range; no descriptor. Rule: 3. The MCP summary counted every notification without an exception as a skipped project, so it now also requires no descriptor.
- Decision: `loweringCensus.runtimeChangeCalls` -> counts calls a row matches inside the pair's interval. Alternatives: counting `CallIdentity.RuntimeChanged`, which also drops suppressed members. Rule: 4.
- Both new samples have an SDK-style project on the legacy side, with a `.sln` so the existing harness finds it. `build.ps1 -Integration` and `parity-run.ps1` restored every `legacy/` project with VS MSBuild; they now do that only for a non-SDK project and use `dotnet restore` for an SDK-style one.
- `IlLoweringParityTests` compares two lowerings of one body, so it now lowers both as a same-runtime pair. Under the 4.8-to-10 interval the new samples' runtime-changed calls would be side-specific and the two lowerings could not be proved equal.
- `PureCatalogue.IsX87` is gone; `SideRuntime` reads the platform. README's scope line and the CLI help are P2-057's.
- `IlLowererTests.CallIdentitiesMatchTheOperationLowering` walks every sample and compares the callees the two lowerings name. `same-runtime-cleanup`'s `switch` expression ends in a discard arm, and Roslyn's control-flow graph keeps its no-match block (a `SwitchExpressionException` constructor) behind a branch that is never taken, which the IL does not have. The solver proves the pair and the two lowerings Equivalent, so the method is listed in that test as a known difference instead of changing the lowering here.
