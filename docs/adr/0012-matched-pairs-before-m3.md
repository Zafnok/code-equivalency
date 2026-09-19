# ADR 0012: What matched pairs report before M3-001 wires a real verification backend

Status: proposed (2026-09-18)

## Context

M2-002 ("Symbol enumeration and Added/Removed end to end") requires: "Program.Main
registers `CSharpFrontend`. Running `equiv compare` on `samples/identical` exits 0 with
a SARIF containing zero results" (acceptance criterion 4).

`samples/identical`'s legacy and modern `Calculator` both declare `Add` and `Max` with
identical signatures. Once `CSharpFrontend` is registered, `StableIdentityMatcher` will
put both in `MatchResult.Pairs` — this is new: with `frontends: []` (the state before
this ticket), `Equiv.Cli.FrontendRouter.Route` always returned `null` and
`CompareCommand.Run` never got as far as building a `MatchResult`, so `Pairs` was always
empty in practice.

`CompareCommand.BuildResults` (M1-005, tested by ~15 cases in
`CompareCommandTests.cs`) unconditionally calls `backend.Verify(pair, options)` for
every pair and adds one `VerificationResult` per pair — this is how
`Compare_WritesSarifAndExits0WhenAllEquivalent` etc. already work, and
`SarifReportWriter.Write` (M1-004) turns every `VerificationResult` into exactly one
SARIF `Result` — there is no "verified with no result" path.

`Equiv.Cli.NoBackend.Verify` unconditionally throws `InvalidOperationException`,
pinned by `NoBackendTests.NoBackend_Throws`. Its own doc comment says this is
"Unreachable in practice: with no frontend configured, `FrontendRouter.Route` always
rejects before any `ProcedurePair` could reach `Verify`" — a precondition this ticket
removes.

Given all three of these (AC4's "zero results", `BuildResults`' unconditional per-pair
`Verify` call, and `NoBackend`'s unconditional throw), running the real `equiv compare`
binary on `samples/identical` after this ticket either crashes (current `NoBackend`) or
produces two `EQ003 Unknown` results (any non-throwing placeholder), never "zero
results" — I cannot satisfy the acceptance criterion without reversing one of the other
two already-decided, already-tested contracts, which CLAUDE.md and `equiv-decide` both
say is not mine to do silently.

## Decision

Not decided yet — this ADR lays out the options for the user to pick from before M2-002
continues.

## Why

- `BuildResults`' pairs loop is real M1-005 behavior with direct test coverage; changing
  its shape now would be a regression, not a detail.
- `NoBackend`'s throw is likewise pinned by its own test and doc comment; it was correct
  under the "no frontend yet" assumption that held from M1-005 until this ticket.
- M2-002's own goal statement says "matched pairs still get no verdict (the backend is
  not wired until M3)" — consistent with the spirit of AC4, but the concrete mechanism
  for "no verdict" (skip vs. report `Unknown`) was never decided anywhere.

## Rejected (not rejected — offered as options)

1. **Change `NoBackend.Verify` to return `Unknown(reason, "not wired until M3-001")`
   instead of throwing.** Needs a new `UnknownReason` member (VERIFICATION-MODEL.md
   section 6 currently lists exactly `timeout`, `opaque`, `unmatched overload` for
   EQ003), and rewrites `NoBackendTests.NoBackend_Throws`. `samples/identical`'s SARIF
   then has two `EQ003` results, not zero — AC4's wording would need correcting too.
2. **Change `CompareCommand.BuildResults` to skip the per-pair `Verify` call entirely
   until a real backend exists**, e.g. gated on `backend is not NoBackend`, or by
   removing the loop until M3-001 restores it (already flagged as an M3-001
   carried-forward item in ROADMAP.md for an unrelated reason — the `Verify` signature
   itself changes shape then). Satisfies AC4 literally (zero results for `identical`)
   but touches ~15 existing `CompareCommandTests` cases that exercise this exact loop
   and would need re-justifying, and needs a defensible way to tell "no backend yet"
   from "a real backend that legitimately has nothing to report" without a type-check
   hack.
3. **Correct AC4 in the ticket** to say `samples/identical` produces two `EQ003 Unknown`
   results (one per matched pair) rather than zero, keep both `NoBackend` and
   `BuildResults` unchanged in shape, only change `NoBackend`'s throw to a return (folds
   into option 1 without also touching `BuildResults`).
4. **Defer wiring `CSharpFrontend` into `Program.Main` to a later ticket** (M3-001, once
   a real backend exists) and keep this ticket's "one-line change in Program.cs" out of
   scope, satisfying AC4 vacuously (the CLI still never reaches a real `MatchResult`
   with pairs) but leaving `equiv compare` non-functional on real solutions for another
   milestone, which conflicts with this ticket's own goal ("With this ticket `equiv
   compare` on the samples produces real EQ004/EQ005 results").

## Consequences

Whichever option is picked changes `docs/tickets/M2-002-symbol-enumeration.md`
acceptance criterion 4's wording, and options 1/3 change VERIFICATION-MODEL.md section
6's EQ003 reason list. Everything else in M2-002 (enumeration, identity, rename
application, Added/Removed with locations) is unaffected and already implemented
against this same branch.
