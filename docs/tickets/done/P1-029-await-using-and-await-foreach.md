# P1-029 `await using` and `await foreach` lower through their awaits instead of making the body opaque
Status: done (PR #390)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-006, M4-001

## Goal
M4-006 lowers `await e` as the call `await:<awaiter type>`. It left two constructs whole-body
opaque, with reasons `await-using` and `await-foreach`, because their desugaring awaits calls the
CFG does not show: `DisposeAsync()` at the end of an `await using`, and `MoveNextAsync()` and
`DisposeAsync()` in an `await foreach`. A whole-body opaque is a `method` scoped Unknown, the kind
that claims nothing. On `jellyfin-13023` the reason `await-using` is in 83 bodies per side and alone
keeps 13 of 573 changed pairs (2.3%) opaque, with no owner. Server code on modern .NET is where
these constructs live, and it is what a version upgrade touches.

When done, both constructs lower to the calls and awaits the compiler emits, in order, and the two
reasons are gone except for the cases the Size guard names.

## Spec references
VERIFICATION-MODEL.md section 3 (the `async` paragraph; `using` and `foreach` through the CFG,
M4-001), ADR 0018 (calls and positions), ADR 0029 (whole-body opaques),
`docs/tickets/IOPERATION-COVERAGE.md`, `docs/tickets/done/M4-006-await-as-a-call.md`,
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (where the two reasons are raised).

## Acceptance criteria (all must hold; nothing beyond them)
1. `await using (var x = e) { body }`, and the declaration form, lower as the synchronous `using`
   does, with the disposal being the call `DisposeAsync()` on the resource followed by an await of
   its result (`await:<awaiter type>`), on both the normal and the exceptional exit, and skipped
   for a null resource of a reference type.
2. `await foreach (var x in e) { body }` lowers as the enumerator loop `foreach` does, with
   `GetAsyncEnumerator(...)`, an awaited `MoveNextAsync()`, `get_Current`, and an awaited
   `DisposeAsync()` in the `finally`. A `WithCancellation` or `ConfigureAwait` on the collection is
   an ordinary call whose result is enumerated.
3. Two bodies with the same `await using` or `await foreach` and a changed statement inside are
   compared on that statement: a new sample `async-disposal` has one Equivalent pair and one
   Divergent pair for each construct. Snapshots checked in.
4. A pair whose legacy side has `using` and whose modern side has `await using` is not Equivalent:
   the two make different calls.
5. On a `--lower-only` run of `jellyfin-13023`, the reason `await-using` is in at most 10 bodies per
   side, or Notes records what the rest are.
6. `IOPERATION-COVERAGE.md` and VERIFICATION-MODEL.md section 3 are updated, and the lowering
   oracle's generator gains both constructs if it can express them; if not, Notes says why.

## Files
`src/Equiv.Frontend.CSharp/Lowering/` (the lowerer and its exception collaborator), its tests,
`samples/async-disposal/**`, `docs/tickets/IOPERATION-COVERAGE.md`, `docs/VERIFICATION-MODEL.md`.

## Tests
`AwaitUsing_DisposesThroughAnAwaitedCall`, `AwaitUsing_DisposesOnTheThrowingExit`,
`AwaitUsing_SkipsANullResource`, `AwaitForeach_AwaitsMoveNextAndDisposes`,
`AwaitForeach_WithCancellationIsACallOnTheCollection`, `UsingAgainstAwaitUsing_IsNotEquivalent`, and
the `async-disposal` snapshot.

## Size guard
Pattern-based disposal on a `ref struct`, an `await using` over several resources in one statement
if the CFG does not order them, and the IL lowering: leave them opaque with their reason and say so
in Notes. Iterators (`yield`) are not this ticket.

## Out of scope
Iterators and async iterators as producers (reason `iterator`). The compiler's state machine.
`ConfigureAwait` semantics beyond "an ordinary call".

## Notes
- From the 2026-10-03 improvement review (its first priority names async/await). Filed directly
  rather than through P1-028 because the constructs and their desugaring are already known.
- Surprise: the Goal's premise does not hold on Roslyn 5.9. The control flow graph does show the awaits: it wraps the
  `DisposeAsync()` of an `await using` and the `MoveNextAsync()` and `DisposeAsync()` of an `await foreach` in implicit
  `IAwaitOperation`s. So the lowering is the removal of the two whole-body opaques plus the two decisions below, and no
  desugaring of its own. The exception collaborator (`ExceptionLowerer`) needed no change.
- Decision: the awaiter of an implicit await -> the result type of the awaited type's own parameterless `GetAwaiter`
  (`ITypeSymbol.GetMembers`); one that only an extension supplies is opaque with reason `Await`, as an await with no
  named awaiter already is. Alternatives: `GetAwaitExpressionInfo(UsingStatementSyntax)` and
  `ForEachStatementInfo.MoveNextAwaitableInfo` / `DisposeAwaitableInfo` (three lookups, and the loop's two awaits share
  one syntax node, so telling them apart needs the awaited method as well). Rule: 4.
- Decision: the `default` the compiler passes for the optional `CancellationToken` of `GetAsyncEnumerator` -> the
  constant element 0 of its sort (`IDefaultValueOperation { IsImplicit: true }` whose syntax is the loop). As the
  `DefaultValue` opaque it was, it had no fingerprint (its syntax is a statement), so every `await foreach` pair was
  Unknown(Opaque) and criterion 3 could not hold. It is one value on both sides and only ever that call's argument, so
  nothing reads a field of it. A struct `default` written in source is unchanged (P2-003). Alternatives: give statement
  syntax a fingerprint in `FragmentFingerprinter` (wider, and adds a `threw` edge to a value that cannot throw), a
  nullary `IrPure`. Rule: 4. VERIFICATION-MODEL.md section 3 and the `DefaultValue` row say so.
- Decision: where criterion 4's test lives -> `AwaitEquivalenceTests` in `Equiv.Tests.Integration`, beside M4-006's
  verdict tests, since it needs the backend. It is Divergent, not only "not Equivalent". Rule: 1.
- Size guard: nothing tripped, and the reasons `await-using` and `await-foreach` no longer exist. Several resources in
  one statement are nested by the CFG, last acquired disposed first (`AwaitUsing_SeveralResourcesAreDisposedInReverseOrder`).
  A pattern-based `DisposeAsync` is called on the resource itself, on a struct with no null test
  (`AwaitUsing_AStructResourceIsDisposedWithoutANullTest`, `AwaitUsing_AnAwaiterFoundOnlyByExtensionIsOpaque`); a `ref struct`
  resource lowered the same way in a scratch run and is not pinned by a test. The IL lowering is untouched.
- An `Await` opaque stands for the whole await, its operand included, as M4-006 left it: the `DisposeAsync()` call
  under an await whose awaiter is not found is not emitted separately.
- Criterion 5, `--lower-only` on `jellyfin-13023` (legacy `5e8c0fe40c0e`, modern `ceb850c77052`), this branch at
  a027b08 plus the comment-only edits of the next commit, 2026-10-04, 436 s, exit 0: `await-using` 0 bodies per side
  (83 on 2026-10-03), `await-foreach` 0, and no `Await` opaque on either side, so no implicit await met an extension
  awaiter. Whole-body opaque pairs 94 of 14,503 (182 on 2026-10-03, which also predates other tickets); `iterator` 64
  per side, unchanged. `DefaultValue` is in 300 bodies per side (261): the bodies that were one `await-using` opaque
  now show the opaques inside them. Changed pairs 162, without opaque 24, whole-body opaque 33; no changed reason set
  names an await reason.
- Criterion 6, the lowering oracle: its generator cannot express either construct, and gains neither. Both are legal
  only in an `async` method, and every method `LoweringOracleGen.Method` renders is synchronous, returning `int`,
  `long`, `bool` or `void`. That generator is shared: `PairGen` and the mutation operators build the differential
  soundness gate's pairs from the same methods, and `CongruenceSoundnessTests` runs them, each invoking the compiled
  method and reading its result directly. An async method shape means a task-returning `OracleMethod`, task unwrapping
  in every one of those consumers, and a model of an async enumerator and of awaits in the call oracle: a change to
  `tests/Equiv.TestSupport` and the soundness gate, outside this ticket's Files. What the oracle would have checked
  is pinned by interpreter runs instead: the trace order of the disposals and their awaits, and the skipped null resource.
- Local run: three other samples (`version-bump`, `webapi-basic`, `runtime-row-framework-only-change`) fail
  `SamplesEndToEndTests` in this worktree because their `modern/` projects were never restored here. Not this change;
  CI restores every sample.
- `IlLoweringParityTests` lists the sample's four methods, on both sides, as known differences for the reason
  `business-layer`'s `ConfirmAsync` is one: an `async` method's IL is its state machine's kickoff, which the IL lowering
  leaves opaque. They joined the test's scope because their source lowering now holds no opaque.
