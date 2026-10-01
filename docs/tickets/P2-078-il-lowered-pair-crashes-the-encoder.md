# P2-078 Ill-sorted IR from the IL lowering no longer crashes the encoder
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-016, P1-017

## Goal
P1-018's Git Extensions run with `--il-fallback` exits 5. One pair,
`GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()`, is Unknown(timeout) on the
IOperation lowering. Lowered from IL, it makes the encoder throw a `Z3Exception`: "domain sort
System.Drawing.PointF and parameter System.Drawing.Rectangle do not match", from
`FragmentEncoder.EncodeInstruction` at `Context.MkSelect`. So the IL lowering emitted a map read
whose index has another sort than the map's key, which is IR the IOperation lowering never
produces. `IlLowerer` checks `IrValidator.Validate` only in a `Debug.Assert`, so a Release run hands
the IR straight to the solver. Find the ILAst shape, lower it correctly or as an `IrOpaque`
(ADR 0039: what cannot be resolved is an opaque, never a new identity), and make sure ill-sorted
IL-lowered IR can never reach the encoder again.

## Spec references
ADR 0039 (the IL lowering follows the IOperation lowering's refusals), ADR 0023 (a pair-level crash),
ADR 0015 and ADR 0018 (heap maps and their keys), `src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.Heap.cs`,
`src/Equiv.Frontend.CSharp/Lowering/Il/IlFallback.cs`, `src/Equiv.Core/Ir/IrValidator.cs`,
`docs/tickets/IL-COVERAGE.md`, `docs/runs/2026-10-01-il-fallback-verdicts.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A test in `tests/Equiv.Frontend.CSharp.Tests` lowers, from IL, a C# method written for the test that
   produces the same ILAst shape as the corpus pair. Find the shape with `--il-fallback --lower-only`
   on the pair, and write its ILAst keys in `## Notes` (keys and type names only, no corpus source).
   Before the fix the test's IR is rejected by `IrValidator.Validate` or makes `Z3Backend.Verify`
   throw.
2. `IrValidator` reports a map read or write whose index type is not the map's key type. If it
   already does, say so in `## Notes` and skip this criterion.
3. The IL lowering lowers that shape to well-sorted IR, or to an `IrOpaque` whose reason is its ILAst
   key. `IL-COVERAGE.md` gains or changes the row.
4. `IlFallback.Choose` keeps the IOperation bodies when either IL-lowered body fails
   `IrValidator.Validate`, in Release as well as Debug, and logs the reason at `debug`
   (`keeps its operation lowering: il-invalid-ir`). A pair never crashes because of the fallback.
5. M0-012's IL mode (P1-017) generates the shape of criterion 1, or `## Notes` says why its
   generator cannot and which test covers it instead.
6. A `--il-fallback` `full` run of `gitextensions-8522` has no entry in `properties.unverified`.
   Record the pair's new verdict in `## Notes`.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Il/` (the lowering and `IlFallback.cs`), `src/Equiv.Core/Ir/IrValidator.cs`
(only if criterion 2 applies), their tests, `docs/tickets/IL-COVERAGE.md`, the differential gate's
generator (only if criterion 5 applies).

## Tests
`IlLowererTests.CorpusShapeLowersToWellSortedIr`, `IlFallbackTests.InvalidIlBodiesKeepTheOperationLowering`,
`IrValidatorTests.MapIndexMustMatchTheMapKey` (if criterion 2 applies).

## Size guard
More than one ILAst shape changed means the ticket has been misread. Other ill-sorted shapes that
criterion 4 now catches are findings: list them in `## Notes`.

## Out of scope
Turning `--il-fallback` on by default. The Divergents the fallback adds (P2-073 to P2-075 own the
runtime rows they cite). Timeouts on IL-lowered pairs (P2-050).

## Notes
- Found by P1-018 (`docs/runs/2026-10-01-il-fallback-verdicts.md`).
- Also seen there, for whichever ticket next proposes turning the default on:
  `JenkinsIntegration.JenkinsAdapter::FormatToGetJson(string,bool)` is Divergent (EQ006) on the
  IOperation lowering and Unknown(timeout) once lowered from IL. ADR 0039's rule compares unshared
  opaques only, so it replaced bodies that decided with bodies that time out.
