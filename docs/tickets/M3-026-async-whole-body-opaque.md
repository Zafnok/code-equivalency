# M3-026 An `async` method is a whole-body opaque instead of ill-typed IR
Status: done (PR #141)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-022

## Goal
`IrLowerer.Lower(IMethodBodyOperation, ...)` (`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`)
gives an `async Task<T>` method's procedure a return type of `sort "System.Threading.Tasks.Task`1"`
(`Signature` maps `method.ReturnType` directly) but lowers `return e;` to `ret` of `e`'s own type —
`bv32` for `int`. `IrValidator` reports IR007, `"types do not fit: ret %$3"`. In a Debug build this
trips `IrLowerer`'s `Debug.Assert(IrValidator.Validate(procedure).IsEmpty, "lowered IR must
validate")` (line 110) and the process fails fast; in Release the ill-typed IR reaches
`IVerificationBackend` untested. An `async Task` method (no result) validates today, because there
is no `ret` type to mismatch, but its `await` desugaring still leaves a spurious `missing-return`
opaque alongside its real ones.

Found during M3-014 (`docs/tickets/M3-014-lowering-census.md`, Notes; PR #127). M3-014's size guard
ruled out lowering changes, so `samples/business-layer`'s `OrderService.ConfirmAsync` was written as
`async Task` instead of `async Task<T>` to avoid tripping the assert, and this ticket was filed
separately.

This ticket does not lower `await`. It makes an `async` method of either shape one whole-body
`IrOpaque`, exactly like `foreach`, `using` and `lock` already are, so the IR it produces validates
and the outcome is honestly `Unknown` instead of silently wrong. M4-006 replaces this with real
lowering.

## Spec references
ADR 0014 (an `IrOpaque` makes the outcome unknown; a whole-body opaque is the existing pattern for
an unsupported construct, applied here to a new one); `docs/tickets/IOPERATION-COVERAGE.md`;
`docs/tickets/M3-013-pair-failures-exit-5.md` Out of scope (names turning this same `Debug.Assert`
into a Release-mode throw as a later step; this ticket removes the one case that trips it today
without touching the assert itself); `docs/tickets/M4-006-await-as-a-call.md`.

## Design
In `IrLowerer.Lower(IMethodBodyOperation body, ...)`, add `method.IsAsync => "async"` to the
`wholeBody` switch (`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs:88-95`), ahead of the
`foreach`/`using`/`lock`/`catch-filter` checks so an async method never reaches them. `Opaque(method,
renames, "async", span)` already derives the procedure's return type from `Signature(method)`, the
same call `Lower` itself uses, so the opaque value's sort matches the declared `Task`/`Task<T>`
return type by construction and IR007 cannot recur here. No change to `Signature`, `TypeMapper`, or
the validator.

## Acceptance criteria (all must hold; nothing beyond them)
1. A method with `async` on its declaration (`Task`, `Task<T>`, or `void`) lowers to one block whose
   only instruction is `IrOpaque` with reason `async`, matching the shape `WholeBodyIsOneOpaque`
   already asserts for `foreach-enumerator`/`using`/`lock`/`catch-filter`. By-ref and out parameters
   are still returned as outs, per `WholeBodyOpaqueKeepsByRefParametersAsOuts`.
2. `IrValidator.Validate` reports no diagnostics for a lowered `async Task<T>` method. The
   `Debug.Assert` in `IrLowerer.Lower` does not fire for any async method in `samples/` or the test
   fixtures.
3. An `async Task` method no longer reports `missing-return`, `Await`, or any other opaque reason
   inside its body: the whole body is the single `async` opaque, nothing else.
4. `docs/tickets/IOPERATION-COVERAGE.md` gains a row for `Await` (there is none today, per M3-014's
   Notes), following the `ForEachLoop` row's pattern: status `opaque: whole body, reason async: an
   async method's state machine is not modelled (ticket M4-006)`, pointing at this ticket's tests.
5. `samples/business-layer`'s `OrderService.ConfirmAsync` (`async Task`) lowers with exactly the
   `async` opaque reason and nothing else; its shape does not otherwise change, so the ticket does
   not need to touch the sample source.
6. `LoweringCensusTests.BusinessLayerCensusSnapshot`'s `.verified.txt` is re-approved: `Await: {legacy:
   1, modern: 1}`, `PropertyReference: {legacy: 4, modern: 4}` and `missing-return: {legacy: 1,
   modern: 1}` each drop by one (`ConfirmAsync`'s share), and a new `async: {legacy: 1, modern: 1}`
   reason appears. `pairsWholeBodyOpaque` rises from 18 to 19; `pairsWithoutOpaque` (2) is unchanged,
   since `ConfirmAsync` already carried an opaque before this ticket, just not a whole-body one.
   Every other row is unchanged. This is a Verify snapshot re-approval, not a hand edit.
7. `samples/business-layer/README.md`'s `ConfirmAsync` row needs no change: "Today" is already
   `Unknown` and "Unlocked by"/"Construct lowered by" already name `M3-015`/`M4-006`. If, once this
   lands, `ConfirmAsync`'s actual construct-level reason differs from what the row implies, correct
   only that cell.

## Files
- `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`
- `tests/Equiv.Frontend.CSharp.Tests/Lowering/IrLowererTests.cs` (`WholeBodyIsOneOpaque` gains rows)
- `docs/tickets/IOPERATION-COVERAGE.md`
- `tests/Equiv.Tests.Integration/LoweringCensusTests.BusinessLayerCensusSnapshot.verified.txt` (via
  Verify re-approval, once M3-014/PR #127 has merged and the sample exists on `main`)
- `samples/business-layer/README.md`, only if criterion 7's exception applies

## Tests
- `WholeBodyIsOneOpaque` (Theory): add `"static async System.Threading.Tasks.Task<int> M() { await
  System.Threading.Tasks.Task.Delay(0); return 1; }", "M", "async"` and `"static async
  System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Delay(0); }", "M", "async"`.
- `AsyncTaskOfTValidates`: lowering the `Task<T>` case above produces IR with an empty
  `IrValidator.Validate` result (regression test for IR007; runs even in Release since it calls the
  validator directly rather than relying on the assert).
- `AsyncMethodNeverReportsAwaitOrMissingReturn`: the `async Task` case's only opaque reason is
  `async`, not `Await` or `missing-return`.
- ~~`WholeBodyOpaqueKeepsByRefParametersAsOuts`-style case for an async method with a `ref`/`out`
  parameter.~~ Not possible: C# rejects `ref`/`out`/`in` on an async method's parameter list
  (CS1988), so no such method can exist to lower. See Notes.
- `LoweringCensusTests.BusinessLayerCensusSnapshot` re-approved (criterion 6).

## Size guard
One switch arm in `IrLowerer.cs` plus its tests and the coverage-table row. If `Signature`,
`TypeMapper`, `IrValidator`, or anything in `Equiv.Core` changes, stop: the bug is a lowering gap,
not a type-system gap, and `Opaque`'s existing return-type derivation already avoids it.

## Out of scope
- Lowering `await` for real (M4-006, which needs M4-001 first — waiting for it is not viable here,
  since this ticket must land before M3-013 and before any full verifying run).
- Turning the `Debug.Assert` into a Release-mode throw (`docs/tickets/M3-013-pair-failures-exit-5.md`
  Out of scope names this as a later step; this ticket just removes the one known way to trip it).
- Any other construct that reaches IR007 today; none is known.

## Notes
Ordering (agreed with the user, not re-derived here): this ticket does not block M3-022. The
`equiv-corpus-run` skill's census mode builds `-c Release`, which strips the `Debug.Assert`, and
`--lower-only` never calls `IVerificationBackend`, so the bug cannot crash a census run. Running
M3-022 first is also strictly better: today an async body still reports its real inner opaque
reasons (`Await`, `PropertyReference`, `missing-return`, ...) in the histogram, and this ticket's
fix would collapse every async method into one `async` reason before that histogram is ever taken.
Hence `Depends on: M3-022`, not the reverse.

This ticket must land before M3-013 and before any full (non-`--lower-only`) run on real code —
M4-007, and any corpus run in a verifying mode. Real ASP.NET code is mostly `async Task<T>`; the
IR007 IR would most likely make the Z3 backend throw (untested, since Release has never hit this
path), `CompareCommand` rethrows that today, and the whole run would abort. Even after M3-013 lands
(a pair whose verification throws is skipped, exit 5), every such method would silently lose its
result rather than reporting `Unknown(async)`. So: after M3-022, before M3-013 and before M4-007.

It also removes a Debug-build fail-fast for anyone who adds an `async Task<T>` method to a sample or
a test fixture in the meantime — today that crashes the process instead of producing an `Unknown`
result.

ADR check (`.claude/skills/equiv-adr` bar test): this is row 3, "a temporary state until a later
ticket" — the whole-body-opaque shape is the interim state M4-006 replaces with real lowering, the
same way M2-004's other whole-body opaques predate their own unlocking tickets without an ADR each.
It is not row 1 (ADR 0014 already states the general rule this ticket applies; there is no new
interpretation of ADR 0014 itself to record, only a new construct using its existing mechanism) and
not row 4 (no component boundary, Core contract, verdict meaning, rule id, SARIF shape, or gate
changes). No ADR.

`M4-006` should get a one-line clarification pointing at this ticket as its interim stopgap (it
currently doesn't mention this bug or M3-026 at all). Not made here: this session's scope is this
ticket and `docs/ROADMAP.md` only, so `docs/tickets/M4-006-await-as-a-call.md` is left untouched
pending that decision.

`docs/tickets/M3-014-lowering-census.md` is `Status: done (PR #127)` on branch
`origin/M3-014-lowering-census`, not yet merged to `main` as of this writing. This ticket's Files
and Tests sections assume that PR has merged first (`samples/business-layer` and its census
snapshot must exist to be touched). It has since merged (M3-022, #139), so this ticket proceeds.

Deviation (`.claude/skills/equiv-adr` bar test, row 2): the Tests section's
`WholeBodyOpaqueKeepsByRefParametersAsOuts`-style case for an async method with a `ref`/`out`
parameter cannot be met — C# rejects `ref`, `out`, and `in` parameters on an async method
(CS1988), so there is no such method to lower. Criterion 1's by-ref/out claim holds vacuously
for `async`: `Opaque` derives the outs the same way for every whole-body reason, already proven
by the non-async `WholeBodyOpaqueKeepsByRefParametersAsOuts` test, and `async` does not special
-case that path. No new test stands in for the requested one; the ticket text above is struck
through rather than silently dropped.

Decision (`equiv-decide`): `method.IsAsync` is checked first in the `wholeBody` switch, ahead of
`foreach`/`using`/`lock`/`catch-filter`, per the ticket's Design section, so an async method's
body is never inspected for those other constructs.

Toolchain: this session's container has no .NET SDK, and installing one is blocked by the
egress proxy's organization policy (`builds.dotnet.microsoft.com` denied). `dotnet test`/
`./build.ps1` could not be run locally; the code and test changes were reviewed by hand against
`IrLowerer.cs`'s existing `Opaque`/`Signature` calls instead. Criterion 6's
`LoweringCensusTests.BusinessLayerCensusSnapshot.verified.txt` update is therefore a hand edit,
not a Verify re-approval as the ticket asks for — computed from `LoweringCensus.Compute`'s logic
(`src/Equiv.Cli/LoweringCensus.cs`) and `ConfirmAsync`'s current body (`Order order = await
pending; lastConfirmedId = order.Id;`, one `Await`, one `PropertyReference`, one
`missing-return` today, becoming one `async` reason). CI (`gates (windows-latest)`, `gates
(ubuntu-latest)`) is the actual verification; flagged in the PR description for the user to
re-check its output against this snapshot.

`gates (windows-latest)` (PR #141 first push) failed only on this snapshot: the Verify comparer's
"Received" and "Verified" text bodies printed identically in the log, but the hand edit had
dropped the file's UTF-8 BOM and added a trailing newline the original file did not have
(`.gitattributes` marks `*.verified.*` as `-text`, so git does not normalize this and both bytes
matter). Fixed by restoring the BOM and removing the trailing newline; `git diff --no-index`
against the pre-ticket file now shows only the intended content change. No other test failed.
re-check its output against this snapshot.
