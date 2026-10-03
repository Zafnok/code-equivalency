# ADR 0023: A pair whose verification crashes is reported and skipped, and the run exits 5

Status: accepted (2026-09-21)

## Context
With M3-001 (PR #78) the CLI verifies every matched pair through `Z3Backend`. Any exception on one
pair (a replay that does not diverge, which M3-001 must treat as an encoder bug and fail loudly on,
a `Z3Exception`, an ill-formed lowering) ends the whole `compare` run: no SARIF is written for the
pairs that did verify. System.CommandLine's default exception handler then prints the stack and
exits 1, which ARCHITECTURE.md defines as "divergence", so CI cannot tell a tool bug from a finding.
The M3-001 review made the error name the pair, but left the exit-code contract alone, because
exit codes are a component boundary. The first real run (M4-007) will hit whatever crashes remain,
and one crash should not cost the other verdicts.

## Decision
`compare` catches an exception from `IVerificationBackend.Verify` per pair, except
`OperationCanceledException` and `OutOfMemoryException`. The pair gets no result, because a crash is
not a verdict about the code. Instead the SARIF run records it where SARIF 2.1.0 puts tool failures:
one `invocations[0].toolExecutionNotifications` entry per failed pair (level `error`, the message
naming both identities, the exception in `exception`), `executionSuccessful: false`, and each
failed pair's identity in the run's `properties.unverified`. The other pairs are verified and
reported as usual. The run exits **5, internal error**, whenever any pair failed, ahead of 1 and 2,
because the result set is incomplete and "only divergent" or "all equivalent" would both misstate
it. `Program.Main` maps any other unhandled exception to exit 5 with the message on stderr, instead
of System.CommandLine's exit 1. For the baseline, a pair listed in `properties.unverified` is not
carried over as `absent`: its baseline result is copied through with `baselineState: unchanged` and
`properties.unverified: true`, so a crash never makes a known divergence look fixed.

## Why
- A crash is a fact about the tool, and SARIF already has a slot for it that Code Scanning and the
  SARIF viewers show. Using it keeps the "no parallel result schema" rule.
- A distinct exit code is the only way a CI step can tell "the tool broke" from "the code diverged"
  without parsing SARIF.
- Loud stays loud: an encoder bug is still an error on stderr, an `error` notification, and a
  non-zero exit. It just no longer throws the other verdicts away.
- Without the baseline rule, a pair that crashes after once being Divergent would be reported
  `absent`, which reads as fixed. That would be a silent false Equivalent at the reporting layer.

## Rejected
- `Unknown` with a new `UnknownReason.InternalError`: turns a tool bug into a finding about the
  code, gets a fingerprint and a baseline state, and `--fail-on divergent` would pass the run.
- Keep aborting and only change the exit code: the verdicts that did verify are still lost.
- Exit 1 (divergence) wins over 5 when both happen: a CI log would then read as "found divergences"
  with no sign the run was incomplete.

## Consequences
- ARCHITECTURE.md's exit-code list gains `5 internal error: at least one pair could not be verified`,
  and it outranks 1 and 2. VERIFICATION-MODEL section 6 notes that a failed pair has no result.
- `SarifReportWriter.Write` takes the failures as well as the results; `BaselineComputer` learns
  the unverified-identity rule. Both are Core reporting API changes.
- `IrLowerer`'s `Debug.Assert(IrValidator.Validate(...))` can become a Release-mode throw once
  lowering failures also have somewhere to go. Lowering happens inside `ILanguageFrontend.Analyze`,
  not per pair in the CLI, so that needs its own step (per-pair lowering, or failures in
  `MatchResult`); this ADR covers backend failures and the process-level catch only.
- If accepted, one S ticket implements it, ordered before M3-003, which verifies exit codes end to end.

## Clarifications
- 2026-10-03 (P2-121). A stack overflow is a crash the Decision's `catch` never sees: the runtime ends
  the process there, with no SARIF. For that crash the Decision holds only if the overflow does not
  happen, so no native call may be handed an input on whose size it recurses. The first case was
  `TraceEncoder.Trace`, which gave `Z3_mk_seq_concat` one operand per block that calls (19,485 a side
  for one unrolled pair of `openra-17989`). Z3 makes that a chain as deep as it is long and recurses
  on the depth, and a 1 MB stack overflows between 8,000 and 10,000 operands. The trace is now a tree
  of concatenations of at most 256 operands each: the same sequence, since concatenation is
  associative, and the same term as before for every trace of up to 256 operands. Two other ways to
  survive the pair were weighed and not taken. Verifying on a thread with a larger stack moves the
  limit and leaves the process one larger pair away from the same end. An Unknown for a pair whose
  unrolled size passes a limit is a new way to end a pair, which ADR 0029 decision 5 and ticket
  P2-076 rule out while the work can be made to fit, and here it can.
