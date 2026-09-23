# P2-011 A procedure whose lowering throws is reported and skipped, not the run
Status: todo
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
