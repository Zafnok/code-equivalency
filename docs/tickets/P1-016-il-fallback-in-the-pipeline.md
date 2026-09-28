# P1-016 IL fallback, part 3: `--il-fallback` lowers a pair with an unshared opaque again from IL, and SARIF says which lowering it used
Status: todo
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
