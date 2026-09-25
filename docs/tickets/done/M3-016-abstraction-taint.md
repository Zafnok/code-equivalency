# M3-016 Replay taint, and Unknown results that point at their lines
Status: done (PR #184)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-001, M3-014

## Goal
ADR 0026: keep EQ002 exact once shared fragments (M4-004) and pure operators (M4-002) let the solver
interpret code freely. The replay tracks taint, and only an untainted divergence is Divergent.
This ticket lands the mechanism before any abstraction exists. It is exercised through
call identities with the `opaque:` prefix, which the lowerer does not emit yet.

ADR 0027 decision 4: a reviewer of an Unknown result should read the lines that caused it, not
the whole method. Every opaque node reached and every abstraction depended on becomes a
`relatedLocation`, and the primary location moves to the first one on the modern side. (This
ticket absorbed M3-023 in the 2026-09-21 consolidation: both change `Unknown` and the SARIF
writer, and the abstraction causes are this ticket's `abstractions`.)

## Spec references
ADR 0026; ADR 0027; ADR 0018; ADR 0019; ADR 0010 (partialFingerprints); ADR 0014;
VERIFICATION-MODEL sections 5 and 6; M3-001 replay design.

## Design
Two commits: taint and `Unknown(Abstraction)` (criteria 1 to 6), then causes and locations
(criteria 7 to 10), which read the abstractions the first commit records.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrInterpreter` gains an optional taint predicate over call identities, and it treats `IrPure`
   the same way once that instruction exists. A tainted run marks:
   - the results and `threw` flags of tainted calls;
   - anything computed from a tainted value, through every instruction and phi;
   - after a branch or switch on a tainted condition, every later definition, trace event, `outs`
     value and the outcome of that side.
2. `IrRun` exposes, per observable, whether it is tainted. The existing untainted API and its
   results do not change.
3. `ModelDecoder.Replay` returns Divergent only when some compared observable differs and that
   observable is untainted on both sides. For the trace, it compares the first differing event.
   A tainted-only difference yields `Unknown(UnknownReason.Abstraction, detail)`. The detail names
   the tainting identities. The decoded model travels as a candidate counterexample.
4. `UnknownReason.Abstraction` exists, and SARIF writes `properties.candidateCounterexample` in the
   `CounterexampleText` rendering and `properties.abstractions` (identities and spans) for it.
5. An untainted replay whose compared observables agree is still the M3-001 encoder-bug failure.
6. Tests build IR directly: a divergence only through an `opaque:` call is Unknown(Abstraction); a
   divergence on an untainted return in a procedure that also calls `opaque:` is Divergent; a
   branch on an `opaque:` result taints the rest of the path.
7. `Unknown` carries `ImmutableArray<SourceSpan> Causes` with a side marker. Opaque causes come
   from the reached `IrOpaque` spans. Abstraction causes come from criterion 4's `abstractions`.
8. The SARIF writer emits each cause as a `relatedLocation` whose message is the reason, and sets
   the primary location to the first modern-side cause. With no modern-side cause, it keeps the
   procedure location.
9. `partialFingerprints` and `baselineState` are unaffected. A test runs a baseline round trip
   where only the cause moves and asserts `unchanged`.
10. A test lowers `business-layer` and runs `Z3Backend` on one loop-free method that stays Unknown
    because of an expression-level opaque. Its primary location is that construct's line. M3-003's
    snapshot of the sample then pins this for every remaining Unknown.

## Files
`src/Equiv.Core/Ir/IrInterpreter.cs`, `src/Equiv.Core/Ir/IrRun*.cs`,
`src/Equiv.Core/Verdicts/UnknownReason.cs`, `src/Equiv.Core/Verdicts/Unknown.cs`,
`src/Equiv.Verify.Z3/ModelDecoder.cs`, `src/Equiv.Verify.Z3/Z3Backend.cs` (collect causes),
`src/Equiv.Core/Reporting/*`, tests in the matching projects.

## Tests
- `TaintFlowsThroughDataDependencies`, `BranchOnTaintTaintsTheRestOfTheSide`,
  `UntaintedDivergenceIsDivergent`, `TaintOnlyDivergenceIsUnknownAbstraction`,
  `CandidateCounterexampleIsWritten`, `UntaintedAgreementIsStillAnEncoderBug`, plus a CsCheck property:
  with no taint predicate, results equal the M3-001 interpreter's.
- `UnknownListsEveryReachedOpaqueSpan`, `PrimaryLocationIsFirstModernCause`,
  `NoModernCauseKeepsTheProcedureLocation`, `MovingACauseKeepsTheBaselineUnchanged`,
  `BusinessLayerUnknownPointsAtItsConstruct`.

## Size guard
No change to how `ProductEncoder` encodes anything: taint is a replay concern. The one allowed
backend addition is reporting which opaque nodes were reachable (criterion 7). If more seems
necessary, stop.

## Out of scope
Emitting abstractions (M4-004, M4-002). Code Scanning upload (M3-004's `action.yml`).

## Notes
- Decision: the taint predicate is an optional last parameter of `IrInterpreter.Run` (`Func<CallIdentity, bool>? taint = null`), so every existing caller is unchanged. Alternatives: an overload, a field on `ICallOracle`. Rule: 4.
- Decision: `IrRun.Taint` is an `IrTaint(bool Outcome, bool Value, ImmutableArray<int> Outs, ImmutableArray<int> Trace, ImmutableArray<CallIdentity> Sources)`: the path's taint (which exit and exception, and the absence of an event past the end of the trace), the returned value's, the indices of tainted outs and events, and the tainting identities reached. It is an `init` property that defaults to `IrTaint.None` and takes part in `IrRun` equality. Keeping the path and the value apart matters: a dropped `log()` call after `return opaque(x)` is a real trace divergence even though the returned value is tainted. Alternatives: one flag per run (loses that case), bool arrays sized to the run. Rule: 1.
- Decision: the trace event of a tainting call is itself tainted, which ADR 0026's list does not say. A shared fragment's event stands for the fragment's own calls, and a fragment with no calls has no events at all, so the same fragment on different arguments is not a real trace difference. VERIFICATION-MODEL section 6 is patched. Alternatives: taint only the result and `threw` (a false EQ002 once M4-004 lands). Rule: ADR 0026 (fills a gap).
- Decision: `ModelDecoder.Replay` returns the `Verdict` (`Divergent`, or `Unknown.DependingOn(candidate, abstractions)`), and rung 1 ends the ladder on an Unknown(Abstraction) with an `Inconclusive` step. `ModelDecoder.Compare` returns `None`, `Abstract` or `Real`; `Diverges` is `Real`, so an induction base model whose divergence is tainted is not a counterexample. Alternatives: keep returning `Counterexample` and let the ladder re-classify. Rule: 4.
- Decision: the `opaque:` prefix is `ModelDecoder.OpaquePrefix`, the only reader until M4-004 adds a writer. Rule: 4.
- Decision: an abstraction is `Abstraction(Codebase Side, CallIdentity Identity, SourceSpan? Span)`, with a new `Codebase { Legacy, Modern }` in `Equiv.Core.Verdicts`. `IrCall` has no span, so replayed abstractions carry none until M4-004 emits `opaque:` calls from spanned `IrOpaque` nodes; SARIF writes `span` only when present. `properties.abstractions` is a list of `{identity, side, span?}` with `side` spelled `legacy`/`modern` as the census does. Alternatives: `ImmutableArray<SourceSpan>` per identity, adding a span to `IrCall` (an IR change outside the size guard). Rule: 1.
- Decision: the property "with no taint predicate, results equal the M3-001 interpreter's" is `IrInterpreterTaintTests.TaintNeverChangesAValue`: over 200 generated procedures, a run without a predicate has `IrTaint.None`, and a run with a predicate (all, none or some callees) equals it once its taint is erased. The M3-001 values themselves stay pinned by the unchanged interpreter tests. Alternatives: a frozen copy of the M3-001 interpreter in the test project. Rule: 3.
- Decision: `Unknown.Causes` is `ImmutableArray<UnknownCause>`, with `UnknownCause(Codebase Side, string Reason, SourceSpan Span)`, not a bare `ImmutableArray<SourceSpan>`: the side marker and the reason (the related location's message) belong to each span. An abstraction's cause reason is `abstraction <identity>`. Related locations carry no `id`. Alternatives: parallel arrays. Rule: 1.
- Decision: rung 1 lists every opaque node some input reaches, not only the ones its first model reaches: while the solver finds an input that reaches an unlisted node, it adds what that input reaches, and an unsatisfiable or unknown query ends the search. That is at most one extra query per opaque node. An induction rung that fails on an opaque lists the nodes its obligation's model reaches, without a search. Alternatives: the first model only (misses an opaque on the other branch), every syntactically reachable block (lists dead code). Rule: ADR 0027 decision 4.
- Decision: causes are listed each line once (unrolling copies a node), legacy side first, then by path, line and column, so "the first modern-side cause" is the earliest modern line. Rule: 3.
- Decision: an opaque Unknown's detail names each `side: reason` once, without the `at path line:column` it had. The lines are in the causes, and the detail is part of the result fingerprint (ADR 0010), so a moved opaque node now baselines as `unchanged` rather than `updated` (criterion 9 holds for real runs, not only for hand-built verdicts: `UnknownCauseTests.MovingAnOpaqueNodeKeepsTheBaselineUnchanged`). The ladder step's detail uses the same text. `FixtureTests.OpaqueDetailNamesTheReachableOpaque` now expects `old: CompoundAssignment` and the cause's span. VERIFICATION-MODEL section 6 is patched. Alternatives: keep the lines in the detail (a moved node is `updated`). Rule: ADR 0027 decision 4.
- Note: locally, the `webapi-basic` integration tests fail in a fresh worktree because that sample's legacy NuGet packages are not restored (`System.Web.Http` unresolved); CI restores them. No other integration test fails.
