# M3-014 Lowering census, `--lower-only`, and the `business-layer` sample
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-006

## Goal
Measure the Unknown rate before optimising it (ADR 0027). Every run writes a lowering census into
the SARIF, and `--lower-only` produces that census without a backend. With it, the real pair can be
measured in M3-022 while M3-002 is still in progress. A new sample of typical service-layer code
gives every later precision ticket a checked-in ratchet.

## Spec references
ADR 0027; ADR 0014; ADR 0006 (SARIF only); ARCHITECTURE.md (CLI, exit codes);
`docs/tickets/IOPERATION-COVERAGE.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `run.properties.loweringCensus` holds these fields:
   - `procedures`: `{legacy, modern}`
   - `matchedPairs`
   - `pairsWithoutOpaque`
   - `pairsWholeBodyOpaque`
   - `pairsCongruent`: always 0 until M3-015
   - `opaqueByReason`: `{reason: {legacy, modern}}`, sorted by reason

   Counts are per procedure body. A whole-body opaque counts once under its reason.
2. `equiv compare --lower-only` loads, matches and lowers. It writes a SARIF log holding the census
   and the EQ004 and EQ005 results, never calls `IVerificationBackend`, and exits 0 unless loading
   fails. The option is rejected with exit 3 (usage) when combined with `--baseline` or `--fail-on`.
3. The census is computed in `Equiv.Cli` from the lowered bodies (`IrOpaque` reasons). No frontend
   API change beyond what already returns the bodies. If bodies are not yet returned (M3-003 criterion 2),
   this ticket adds `ProcedurePair.OldBody`/`NewBody` as M3-003 specifies, and M3-003 keeps them.
4. New sample `samples/business-layer/` (net48 legacy, net10 modern) with 12 to 16 methods of
   typical service code. It must include each of: property reads, `foreach` over `List<T>`, a LINQ
   chain with a lambda, `decimal` arithmetic, `int.TryParse(s, out var n)`, a guard
   `throw new ArgumentNullException`, `async`/`await`, an `is T t` pattern, `using`, `lock`, and an
   interpolated string. Most methods are unchanged. Three are cosmetic refactors (a renamed local,
   an extracted variable, an inverted guard). One is a real divergence:
   `Math.Round(x, 2)` becomes `Math.Round(x, 2, MidpointRounding.AwayFromZero)`. The README lists
   each method's target verdict and the ticket expected to unlock it (M3-010, M3-011, M3-015 to M3-021).
5. An integration test runs `--lower-only` on `business-layer` and snapshots the census (Verify).
   The snapshot is expected to change in later tickets, and each change is reviewed.
6. ARCHITECTURE.md's CLI section documents `--lower-only`.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/LoweringCensus.cs` (new),
`src/Equiv.Core/Reporting/*` (only to carry the run property bag), `samples/business-layer/**`,
`tests/Equiv.Cli.Tests/*`, `tests/Equiv.Tests.Integration/LoweringCensusTests.cs`,
`docs/ARCHITECTURE.md`.

## Tests
`CensusCountsOpaqueReasonsPerSide`, `WholeBodyOpaqueCountsOnce`, `LowerOnlyNeverCallsTheBackend`,
`LowerOnlyRejectsBaselineAndFailOn`, `BusinessLayerCensusSnapshot`.

## Size guard
No change to lowering. If the census wants a frontend change, stop.

## Out of scope
Fingerprints (M3-015). Locations of Unknown results (M3-023). Running on the user's pair (M3-022).

## Notes
