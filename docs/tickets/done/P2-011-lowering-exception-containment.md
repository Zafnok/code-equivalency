# P2-011 A procedure whose lowering throws is reported and skipped, not the run
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-013, M3-024

## Goal
ADR 0029's containment ladder is project, then method, then line. M3-024 contains load faults to a
project, and M3-013 contains verification faults to a pair. A fault in *lowering* is contained to
nothing. In M3-022's census, one procedure of Git Extensions threw in `IrLowerer` (P2-010), and
the whole `compare` run exited 1 with no SARIF, so 1,577 legacy files produced no census.

After this ticket, an exception from `IrLowerer.Lower` for one side of one pair, other than
`OperationCanceledException` and `OutOfMemoryException`, is recorded like M3-013's pair failure:
a tool-execution notification naming both identities, the pair listed in
`run.properties.unverified`, no result, and exit 5. Every other pair is still lowered and counted
in the census.

## Spec references
ADR 0029; ADR 0023; M3-013 (reuse its failure path and exit code); M3-024 (`unverified`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A frontend test with a lowering fault injected in one of two procedures reports the other
   procedure's census and one notification for the faulting pair.
2. `--lower-only` and full runs both exit 5 in that case.
3. The census counts the failed pair in neither `pairsWithoutOpaque` nor `pairsWholeBodyOpaque`,
   and VERIFICATION-MODEL's census paragraph says so.

## Size guard
One catch per pair in `CSharpFrontend.Analyze` plus the plumbing M3-013 already built. If
M3-013's path does not fit, stop and route it through `equiv-adr`.

## Out of scope
Fixing the P2-010 crash itself.

## Notes

Decision: the frontend reports a lowering fault as a new Core record, `LoweringFailure(Old, New, Exception)`, in a new
`MatchResult.LoweringFailures` (defaulting to empty, like `LegacySkipped`), not as a `ProcedurePair` with null bodies:
`CompareCommand.Lowered` already treats a null body as a frontend bug, and keeping failed pairs out of `Pairs` means
nothing downstream (census per-body counts, `Verified`) has to learn to skip them. ADR 0023's Consequences names
"failures in `MatchResult`" as one of the two options, so this is inside the accepted design.

Decision: in the census, a pair whose lowering threw still counts in `procedures` and `matchedPairs` (it was matched),
and in no per-body count. `LoweringCensus.Compute` gains an `unlowered` count for that; the property's shape is unchanged.

Decision: criterion 1's "frontend test ... reports the other procedure's census and one notification" crosses the
frontend/CLI boundary, so it is proved in two halves: `CSharpFrontendTests.Analyze_LoweringFaultInOnePair_RecordsItAndLowersTheOther`
(the frontend lowers the good pair and records one `LoweringFailure`, via an internal lowering seam on `CSharpFrontend`
that faults on a chosen method, so the test does not depend on P2-010 staying unfixed) and
`CompareCommandTests.Compare_PairThatFailedToLower_IsReportedAsNotificationAndOtherPairsCounted` (census, notification,
`unverified` and exit 5 from that `MatchResult`, `--lower-only` and full).

Decision: notification text is `Lowering <old> against <new> failed: <message>`, M3-013's `Verifying ...` text with the
stage swapped; both now go through one `CompareCommand.PairFailure` helper. README's exit-5 row now says "lowering or
verification crashed".

Deviation: developed on the harness-assigned branch (`claude/sharp-brahmagupta-toor7o`) rather than a new
`P2-011-...` branch, per this session's Git Development Branch Requirements (as M3-013 did).

Toolchain: `dotnet` 10.0.401 is available in this container; `dotnet test` on Equiv.Core.Tests, Equiv.Frontend.CSharp.Tests
and Equiv.Cli.Tests passed locally and `dotnet format --verify-no-changes` is clean on the touched files. The full
`./build.ps1` gate (coverage, mutation, architecture) is left to CI, per the task loop.
