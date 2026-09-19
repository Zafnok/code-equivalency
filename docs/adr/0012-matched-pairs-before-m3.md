# ADR 0012: Matched pairs are skipped, not verified, until M3-001 wires a real backend

Status: accepted (2026-09-18). Partially supersedes M1-005 acceptance criterion 7
(`NoBackend` whose `Verify` throws).

## Context

M2-002 acceptance criterion 4 requires: "Program.Main registers `CSharpFrontend`.
Running `equiv compare` on `samples/identical` exits 0 with a SARIF containing zero
results". The ticket goal says "matched pairs still get no verdict (the backend is not
wired until M3)", and its integration test is named `Samples_IdenticalYieldsNoResults`.

`samples/identical` has two matched pairs (`Add`, `Max`). Before M2-002, `Program.Main`
passed `frontends: []`, so `FrontendRouter.Route` always rejected and no `ProcedurePair`
ever reached `CompareCommand.BuildResults`. M1-005 relied on that: its `NoBackend.Verify`
throws and is documented as unreachable. Registering `CSharpFrontend` makes it reachable,
so the shipped binary would crash on any input with a matched pair.

`BuildResults` calls `backend.Verify(pair, options)` once per pair, and
`SarifReportWriter.Write` (M1-004) turns every `VerificationResult` into exactly one SARIF
result. There is no path for "matched, not verified".

## Decision

Before M3-001, the CLI has no verification backend. That absence is modelled as `null`,
not as a placeholder object:

- `CompareCommand.Create` and `CompareCommand.Run` take `IVerificationBackend? backend`.
- `CompareCommand.BuildResults` adds no results for `matchResult.Pairs` when `backend` is
  `null`. When a backend is present, pair handling is unchanged.
- `Program.Main` passes `frontends: [CSharpFrontend]` and `backend: null`.
- `src/Equiv.Cli/NoBackend.cs` and `tests/Equiv.Cli.Tests/NoBackendTests.cs` are deleted.
- One new test, `Compare_WithoutBackend_SkipsMatchedPairs` in `CompareCommandTests`, covers
  the `null` branch: pairs yield no results, while Added/Removed are still reported.
- M2-002 acceptance criterion 4 keeps its wording.
- M3-001 passes `Z3Backend` from `Program.Main`, makes the parameter non-nullable again,
  and removes the skip branch (added to M3-001's acceptance criteria).

## Why

- It matches the plan's stated intent: M2-002's goal ("no verdict"), acceptance
  criterion 4 ("zero results"), and test name (`...YieldsNoResults`) all say pairs produce
  nothing before M3. Only the mechanism was undecided.
- `null` means "no backend" directly. M1-005's `NoBackend` was an object that must never be
  called, which only held while no frontend was registered.
- The existing `CompareCommandTests` pass a fake backend and are unaffected. The pair loop
  they cover does not change shape.
- The change is temporary, and its removal belongs to M3-001, which already rewrites
  `IVerificationBackend.Verify`'s signature and the M1-005 fakes.

## Rejected

1. **`NoBackend.Verify` returns `Unknown(..., "not wired until M3-001")`.** This needs an
   `UnknownReason` that VERIFICATION-MODEL.md section 6 does not define. It reports a
   missing tool component as if it were a verification outcome, and it contradicts
   acceptance criterion 4 and the ticket goal.
2. **Gate the pair loop on `backend is not NoBackend`.** This produces the same results as
   the decision, but it uses a type check to encode what a nullable parameter states
   directly.
3. **Change acceptance criterion 4 to expect two `EQ003` results.** Same objections as 1.
   It changes the plan to fit the placeholder instead of the other way round.
4. **Defer the `Program.Main` wiring to M3-001.** This breaks M2-002's goal that
   `equiv compare` produce real EQ004/EQ005 results on the samples, and leaves the
   shipped binary unable to accept any real input for another milestone.

## Consequences

- From M2-002 until M3-001, `equiv compare` reports only Added/Removed. A matched pair gets
  no result, not even Unknown. The SARIF from this period says nothing about matched
  procedures, and consumers must not read "zero results" as "all equivalent". This is
  acceptable because nothing ships before M3-004.
- M1-005 acceptance criterion 7 and its `NoBackend_Throws` test are superseded by this ADR.
- M3-001 gains an acceptance criterion that restores a non-nullable backend.
