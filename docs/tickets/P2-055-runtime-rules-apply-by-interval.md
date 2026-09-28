# P2-055 Runtime rules apply only across the runtimes a pair crosses
Status: todo
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
4. Every existing sample's `expected.sarif.json` is unchanged except for `run.properties.runtimes`
   (all current samples are 4.8 against 10).
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
