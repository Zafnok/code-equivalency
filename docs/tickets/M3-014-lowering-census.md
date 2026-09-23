# M3-014 Lowering census, analysed line counts, `--lower-only`, and the `business-layer` sample
Status: done (PR #127)
Effort: L
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-006

## Goal
Measure the Unknown rate before optimising it (ADR 0027). Every run writes a lowering census into
the SARIF, and `--lower-only` produces that census without a backend. With it, the public corpus
can be measured in M3-022 while M3-002 is still in progress. A new sample of typical service-layer
code gives every later precision ticket a checked-in ratchet.

The same run-level report carries the analysed line count of each codebase. The BUSL free tier in
`LICENSE` permits production use only while no analysed codebase exceeds 50,000 lines, measured
per codebase rather than summed across a comparison. Nothing today tells a user which side of that
line they are on, which makes the cap impossible to comply with in good faith. (This ticket
absorbed M3-006 in the 2026-09-21 consolidation, so the counts land before M3-003 approves the
first sample snapshots.)

## Spec references
ADR 0027; ADR 0014; ADR 0006 (SARIF only); ARCHITECTURE.md (CLI, exit codes);
`docs/tickets/IOPERATION-COVERAGE.md`; LICENSE (Additional Use Grant, limit (b));
`docs/adr/0017-licensing-and-ip.md` (Consequences); VERIFICATION-MODEL.md.

## Design
Two run-level reports computed in `Equiv.Cli` after loading and lowering, written as run
properties: the census (criteria 1 to 6) and the line counts (criteria 7 to 12). Land the census
first. The line counts reuse its plumbing into the run property bag.

## Acceptance criteria (all must hold; nothing beyond them)
1. `run.properties.loweringCensus` holds these fields:
   - `procedures`: `{legacy, modern}`
   - `matchedPairs`
   - `pairsWithoutOpaque`
   - `pairsWholeBodyOpaque`
   - `pairsCongruent`: always 0 until M3-015
   - `opaqueByReason`: `{reason: {legacy, modern}}`, sorted by reason

   Counts are per procedure body. A whole-body opaque counts once under its reason.
2. `equiv compare --lower-only` loads, matches and lowers. It writes a SARIF log holding the census,
   the line counts and the EQ004 and EQ005 results, never calls `IVerificationBackend`, and exits 0
   unless loading fails. The option is rejected with exit 3 (usage) when combined with `--baseline`
   or `--fail-on`.
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
   each method's target verdict and the ticket expected to unlock it (M3-010, M3-015, M3-016,
   M4-001 to M4-006).
5. An integration test runs `--lower-only` on `business-layer` and snapshots the census (Verify).
   The snapshot is expected to change in later tickets, and each change is reviewed.
6. ARCHITECTURE.md's CLI section documents `--lower-only`.
7. `equiv compare` prints the analysed line count for the legacy and modern codebases as two
   separate numbers. They are never summed or presented as a single total. The licence measures
   each codebase separately, and a combined figure would misstate compliance in both directions.
8. The same two counts appear in the SARIF output under the run's `properties`, with names that
   say what they measure.
9. The counting rule is stated in one place, documented in `README.md` next to the licence summary
   and in VERIFICATION-MODEL.md, and applied to both sides identically: which files are counted,
   and whether blank and comment lines are included. The number that appears must be the number
   the licence means. `LICENSE` and the README summary must not contradict it.
10. `--dry-run` and `--lower-only` both report the counts without running verification, so a user
    can check where they stand before committing to a full run.
11. The counts appear whether the verdict set is empty or not.
12. No licence enforcement, nag, telemetry or behaviour change is attached to the numbers. They
    are reported and nothing else.

## Files
- `src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/LoweringCensus.cs` (new), the CLI summary output
- `src/Equiv.Core/Reporting/*` (only to carry the run property bag)
- wherever the frontend already knows the loaded document set, for the line count itself
- `samples/business-layer/**`
- `tests/Equiv.Cli.Tests/*`, `tests/Equiv.Tests.Integration/LoweringCensusTests.cs`, affected
  `.verified.txt` snapshots
- `docs/ARCHITECTURE.md`, `docs/VERIFICATION-MODEL.md`, `README.md`

## Tests
- `CensusCountsOpaqueReasonsPerSide`, `WholeBodyOpaqueCountsOnce`, `LowerOnlyNeverCallsTheBackend`,
  `LowerOnlyRejectsBaselineAndFailOn`, `BusinessLayerCensusSnapshot`.
- Unit: line counts for a known fixture pair match a hand-computed number.
- Unit: legacy and modern counts are reported independently and are not summed.
- Snapshot: SARIF run properties carry both counts.
- Integration: `--dry-run` against a `samples/` pair reports counts.

## Size guard
No change to lowering. The only frontend change allowed is exposing the loaded document set (or its
line counts) for criterion 7. If the census wants any other frontend change, stop. If the line count
touches the IR or the matcher, it has drifted.

## Out of scope
- Fingerprints (M3-015). Locations of Unknown results (M3-016). Running the corpus (M3-022).
- Enforcing the line limit, gating on it, phoning home, or printing a licence warning. The tool
  reports a fact; compliance is the user's obligation under `LICENSE`.
- Counting seats. The three-individual cap is not observable from inside the tool and must not
  be guessed at.
- A separate `equiv loc` command.

## Notes
Decision to make when implementing, per `.claude/skills/equiv-decide` (carried over from M3-006):
whether "lines of code" means physical lines in the files the frontend loaded, or non-blank
non-comment lines. Pick one, state it in `README.md` next to the licence summary, and use the same
rule on both sides. The figure only has to be honest and reproducible; it does not have to match
any other tool's definition.

Decision: "lines of code" means lines that hold part of a C# token, so blank lines, comment-only
lines, preprocessor directive lines and code an inactive `#if` excludes do not count. The files are
every syntax tree in the loaded compilations, generated ones included, and each file path counts
once. This is the usual meaning of the licence's term, Roslyn's tokens make it exact and
reproducible, and counting generated files errs toward the larger number. The rule is implemented
once, in `Equiv.Frontend.CSharp/CodeLines.cs`, and stated in README "Licence".
Decision: the counts leave the frontend through `ILanguageFrontend.Analyze`, which now returns
`FrontendAnalysis(Match, Lines)` (`AnalysedLines` has no total). A separate count method would load
each solution twice. `SarifReportWriter.Write` gains an optional `runProperties` bag, which is the
only Reporting change.
Decision: `--dry-run` now goes through `Analyze` to report the counts (criterion 10). It still writes
no SARIF and never calls the backend. Like a normal run, it now exits 3 on a missing `--config` or
`--baseline` file.
Decision: `--fail-on` has no parsed default any more. Null means `divergent`, so `--lower-only`
rejects only an explicit `--fail-on`.
Decision: census `procedures` is matched pairs plus removed (legacy) or added (modern) procedures.
Ambiguous identities are counted on neither side, because `MatchResult` does not say which side they
come from or how many there are, and the CLI reports no result for them either. `opaqueByReason`
counts bodies, not nodes. A body is whole-body opaque when it is one block whose only instruction is
an `IrOpaque`, which is the shape the lowerer gives it, and a pair counts when either side is.
Note: the sample's async method is `async Task ConfirmAsync`, not `async Task<T>`. An `async Task<T>`
method lowers to ill-typed IR: IR007, `ret` of the result type in a procedure typed as the task
sort. That trips `IrLowerer`'s `Debug.Assert` (Debug builds fail fast) and hands invalid IR to the
backend in Release. The size guard rules out lowering changes here, so this is filed separately. An
`async Task` body also gets a spurious `missing-return` opaque.
Note: `business-layer` is not in `SampleLoweringTests`' OperationKind coverage list. It holds kinds
that have no row in IOPERATION-COVERAGE.md yet (`Await`, `DelegateCreation`, `InterpolatedString`
and others), and adding those rows is outside this ticket.
Note: running `Equiv.Tests.Integration` locally without `build.ps1` fails the webapi-basic tests,
because its legacy side needs the MSBuild sample restore that `build.ps1` and CI do.
