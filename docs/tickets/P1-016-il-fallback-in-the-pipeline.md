# P1-016 IL fallback, part 3: `--il-fallback` lowers a pair with an unshared opaque again from IL, and SARIF says which lowering it used
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-015

## Goal
ADR 0039's per-pair rule, applied in a run: with `--il-fallback` (off by default), a matched pair
that is not congruent, and where either side holds an opaque whose fingerprint the other side does
not share, is lowered from IL on both sides. It keeps the IL bodies only if they hold fewer unshared
opaques. Every result on a matched pair says which lowering it used, and the census counts both.
About 5 source files and 3 test files.

## Spec references
ADR 0039; VERIFICATION-MODEL.md section 3.1 and section 6 (`loweringCensus`, `properties.lowering`);
ADR 0024 (congruence first; shared fragments); ADR 0027 (the census); ADR 0038 (the `lower` phase's
progress events).

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv compare --il-fallback` and the MCP `compare` and `lower_only` tools' `ilFallback` flag
   exist, and default to off. Without them, stdout and SARIF are byte-identical to a run before this
   ticket (a snapshot test on `samples/business-layer`).
2. Congruence is decided before the fallback. A congruent pair is never lowered from IL.
3. The fallback runs on a pair exactly when it is not congruent and either side holds an
   `IrOpaque` whose fingerprint the other side lacks. It always lowers both sides. It keeps the IL
   bodies exactly when their count of unshared opaques is lower, and never mixes lowerings in a pair.
4. Every result on a matched pair carries `properties.lowering` (`operation` or `il`) when the run
   used `--il-fallback`. `run.properties.loweringCensus` gains `pairsIlFallbackTried` and
   `pairsLoweredFromIl`, and counts opaque reasons from the lowering each pair kept.
5. A method whose IL cannot be read (emit failure, not found, no body) keeps its IOperation
   lowering. Each such case is one `debug` detail line, and the run never fails because of it.
6. A new sample pair, `samples/il-fallback`, holds one method pair with a lifted `int?` operator and
   one with a nullable conversion. With `--il-fallback` both are lowered from IL, and the snapshot
   records their verdicts, whatever they are.
7. `--execute` replay and P1-008's differential testing are unchanged for IL-lowered pairs: they
   still run the real methods, and a Divergent's model names the same parameters.

## Files
- `src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/LoweringCensus.cs`, the MCP tool registration in `src/Equiv.Cli`
- `src/Equiv.Frontend.CSharp/CSharpFrontend.cs`, `src/Equiv.Frontend.CSharp/Lowering/Il/IlFallback.cs`
- `src/Equiv.Core` result or census properties, only if the property has no home yet
- `samples/il-fallback/**` and its README
- `tests/Equiv.Cli.Tests/CompareCommandTests.cs`, `tests/Equiv.Frontend.CSharp.Tests/Lowering/Il/IlFallbackTests.cs`,
  `tests/Equiv.Tests.Integration/IlFallbackSampleTests.cs` and its snapshot

## Tests
- `IlFallbackTests.ACongruentPairIsNeverRelowered`, `.BothSidesAreRelowered`, `.IlIsKeptOnlyWithFewerUnsharedOpaques`,
  `.AnUnreadableMethodKeepsItsOperationLowering`
- `CompareCommandTests.IlFallbackIsOffByDefault`, `.IlFallbackMarksEachResultsLowering`
- `IlFallbackSampleTests.SampleVerdictsSnapshot`, `.WithoutTheFlagOutputIsUnchanged`

## Size guard
More than 8 source files, or any change to the Z3 backend, means the fallback is leaking past the
frontend.

## Out of scope
Turning the fallback on by default (P1-018). The differential gate's IL mode (P1-017). Catalogue
entries for `Nullable<T>` getters or `string.Format`.

## Notes
- Decision: the flag reaches the frontend as `EquivConfig.IlFallback`, an init-only property with no `equiv.config.json` key, set from `CompareOptions` as `Bound` and `TimeoutMs` are. Alternatives: a parameter of `ILanguageFrontend.Analyze`. Rule: smallest surface; every frontend and test fake keeps its signature.
- Decision: the frontend decides congruence on the two bound fingerprints (equal and not runtime-sensitive), ADR 0024 decision 1 without the CLI's unbound test. A pair the CLI calls congruent is congruent here too, so it is never lowered again (criterion 2); an unbound body does not emit, so it keeps its IOperation lowering anyway.
- Decision: an opaque is unshared when it has no fingerprint or the other body has none equal to it, counted per opaque on both sides: `ProductEncoder`'s rule for which fragments become shared calls. Rule: ADR 0024 decision 2, one definition of "shared".
- Decision: an `async` or iterator method is unreadable (`il-state-machine`) and keeps its IOperation lowering, like the three read failures. Rule: soundness: its IL is the kickoff of its state machine, and two different `async` bodies can have the same kickoff (the same builder calls on a state-machine type of the same name), which would prove them Equivalent. P1-015 already listed `ConfirmAsync` as a known difference for the same reason.
- Decision: `IlLowerer.Lower` takes the x87 flag, and the fallback passes `PureCatalogue.IsX87` of the legacy compilation and `false` for the modern one, as `IrLowerer.Lower` does (P1-015's note for this ticket). Rule: soundness: otherwise an x87 legacy side's floating point would be one function on both sides.
- Decision: a pair that keeps its IL bodies has no `equivalencesApplied`, since the IL lowering applies none. The IL lowering is not given the config's renames, suppressed runtime changes or API equivalences (P1-014 and P1-015 built it on `RenameMap.Empty`): that costs precision (a renamed callee or a catalogued equivalence stays two different uninterpreted calls, so Unknown(Abstraction)), never soundness, since an unsuppressed runtime change is the conservative side. P1-018's corpus run will show whether it matters.
- Decision: an IL body keeps `IlLowerer`'s own procedure identity rather than the renamed IOperation one, so its self-calls still name the procedure. The M4-006 async-mismatch opaque is applied after the fallback, whichever lowering the pair kept.
- Decision: an exception thrown by the IL lowering is not caught by the fallback; the pair takes P2-011's lowering-failure path. Rule: criterion 5 covers IL that cannot be read, and a crash is a tool bug a corpus run should report, not hide in a debug line.
- Decision: `pairsIlFallbackTried` and `pairsLoweredFromIl` follow the census's per-body counts and precede `unknownByScope`, only under the flag; `lowering` is the last result property. Criterion 1 is `IlFallbackSampleTests.WithoutTheFlagOutputIsUnchanged`: `samples/business-layer/expected.sarif.json`, which this ticket does not change, plus a stdout snapshot.
- Decision: the sample's two pairs are `Add(int?, int)` (a lifted `+` against its spelled-out form) and `Wrap(int)` (an implicit nullable conversion against `new int?(x)`). Without the flag both are Unknown(opaque); with it both keep the IL bodies and are Equivalent by `bounded`, since each side compiles to the same `Nullable<int>` getter calls.
- Criterion 7: replay and differential testing find the real methods by the pair's identity (`ReplayDriverFactory`), which the fallback does not change, and the IL bodies have the IOperation bodies' parameters (`BothSidesAreRelowered` asserts it), so a model names the same parameters.
- Deviation: 11 source files, over the size guard's 8, none of them in `Equiv.Verify.Z3`. Beyond the Files line: `CompareOptions` (the option), `EquivConfig` (the flag's way to the frontend), `ProcedurePair`, `VerificationResult` and `SarifReportWriter` (the `lowering` property's way from the pair to SARIF, the Files line's "result properties") and `IlLowerer` (the x87 flag). The fallback's logic is only in `IlFallback`; the rest carries a bool or a string.
