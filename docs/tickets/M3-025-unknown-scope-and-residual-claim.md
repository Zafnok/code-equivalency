# M3-025 Every Unknown states its scope and residual claim; whole-body opaques point at their construct
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-016, M3-024

## Goal
ADR 0029 decisions 3 and 4. A reviewer must be able to tell an Unknown that is unknown on one line
from one that is unknown for a whole method, without reading either. After this ticket every
Unknown carries `properties.scope`. A `line`-scoped one also states what was proved: equivalent on
every input that reaches none of its related locations. That follows from ADR 0014's query 1 and
costs no solver time. Whole-body opaques, until M4-001, M4-003 and M4-008 remove them, point at the
construct that caused them instead of the method body. That shrinks M3-016's relatedLocation from
the method to the statement.

## Spec references
ADR 0029; ADR 0014 (two queries); ADR 0027 decision 4; M3-016 criteria 7 and 8; VERIFICATION-MODEL
section 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrLowerer`'s whole-body opaque for `foreach-enumerator`, `using`, `lock` and `catch-filter`
   uses the span of the first offending operation (the statement, or the `catch` clause), not the
   body. The reason strings do not change. `IOPERATION-COVERAGE.md` rows say so.
2. The IR distinguishes a whole-body opaque from an expression-level one, so that scope can be
   computed without guessing from the span. Use a flag on the `IrOpaque` record or a reason
   prefix, decided with `equiv-decide`. The IR text format round-trips it.
3. `Unknown` gains `UnknownScope Scope` with values `Line` and `Method`. `Project` is not a result
   (ADR 0029: skipped projects are `unverified`), so it is not a value.
   - `Line`: reason `Opaque` or `Abstraction`, query 1 unsatisfiable, and no cause is whole-body.
   - `Method`: everything else (whole-body opaque, `Timeout`, `UnalignedLoop`, `Recursion`,
     `Unbound`, `UnmatchedOverload`).
4. The SARIF writer emits `properties.scope` (`line` or `method`) on every EQ003 result. For
   `line` it also emits `properties.residualClaim` with the fixed text
   `equivalent unless a relatedLocation is reached`, and the message ends with one sentence saying
   the same in words.
5. `partialFingerprints` and `baselineState` do not change with scope. A test moves an Unknown
   from `method` to `line` against a baseline and asserts `unchanged`.
6. When a run produces verdicts, `run.properties.loweringCensus.unknownByScope` is
   `{line, method}`. With `--lower-only` it is absent.
7. `business-layer`'s census snapshot shows `pairsWholeBodyOpaque` no higher than before this
   ticket. A test asserts that each whole-body reason has an owner in a checked-in table
   (`WholeBodyReasonOwners`: reason to ticket id), so a new whole-body reason fails the build until
   it gets an owner.
8. The behaviour matches VERIFICATION-MODEL sections 1, 6 and 7 as the PR that accepted ADR 0029
   wrote them, including the residual-claim property test of section 7
   (`LineScopedResidualClaimHolds`).

## Files
- `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (spans only)
- `src/Equiv.Core/Ir/IrOpaque.cs` (or the record's file), `src/Equiv.Core/Ir/IrText*.cs`
- `src/Equiv.Core/Verdicts/Unknown.cs`, `UnknownScope.cs` (new)
- `src/Equiv.Verify.Z3/Z3Backend.cs` (report whether query 1 was unsatisfiable, which it already computes)
- `src/Equiv.Core/Reporting/SarifReportWriter.cs`, `src/Equiv.Cli/LoweringCensus.cs`
- tests in the matching projects; the `business-layer` snapshots
- `docs/VERIFICATION-MODEL.md`, `docs/tickets/IOPERATION-COVERAGE.md`

## Tests
- `WholeBodyOpaqueSpanIsTheConstructNotTheBody` (one per reason), `WholeBodyFlagRoundTripsInIrText`,
  `OpaqueOffPathUnknownIsLineScoped`, `WholeBodyUnknownIsMethodScoped`, `TimeoutIsMethodScoped`,
  `LineScopeCarriesTheResidualClaim`, `ScopeChangeKeepsTheBaselineUnchanged`,
  `CensusCountsUnknownsByScope`, `EveryWholeBodyReasonHasAnOwner`, `LineScopedResidualClaimHolds`
  (CsCheck property).

## Size guard
No new solver query. If computing scope seems to need one, the ticket has been misread: query 1's
result is already known when query 2 runs.

## Out of scope
Removing whole-body opaques (M4-001, M4-003, M4-008). Narrowing a timeout below the method.
Project-level containment (M3-024).

## Notes
