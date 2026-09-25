# P1-007 Rung 1 inlines self-calls that read or write the heap
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-002, M3-007, P1-005, P1-006

## Goal
`IrUnroller.InliningObstacle` refuses to inline a self-call in three heap cases, and none of them is
needed once P1-005 and P1-006 have landed:

- "an input is keyed by an array variable" (any `array.*`/`length.*` input). M3-002 added it while
  array maps were per variable (ADR 0015). P1-006 keys them by the array reference, like `field.*`,
  so the reason no longer holds and the message is out of date.
- "it has a by-ref parameter", raised for the synthesised `field.*`/`array.*` maps. M3-002's rule
  was "every parameter `In`", written when heap maps were `In`. M3-007 made them `Ref`, so since
  PR #161 every self-recursive procedure that touches a field or an array element is refused
  here, even when it only reads them. Dropping only the array-variable arm would unblock just the
  procedures whose one heap input is `length.*`.
- "it writes the heap". The inlined copy's writes were lost because nothing carried the callee's
  final heap back to the caller. P1-005 gives the self-call a (before, after) pair per heap map,
  so the copy can take the heap from `before` and hand its final heap back through `after`.

After this ticket, rung 1 inlines a self-recursive procedure that reads or writes fields and arrays.
Its counterexamples stay real and its proofs stay exact (VERIFICATION-MODEL section 5: "Rung 1
inlines it instead, so its counterexamples are real"). A source-language `ref`/`out` parameter
still blocks inlining.

## Spec references
VERIFICATION-MODEL.md section 2 (synthesised inputs and their kinds) and section 5 (rung 1,
recursion); M3-002's `Decision:` line on when inlining is exact; M3-007; P1-005 criterion 1 (the
heap pairs on `IrCall`); P1-006's Notes (the last-but-one bullet names this ticket's gap).

## Acceptance criteria (all must hold; nothing beyond them)
1. `InliningObstacle` has no `array.*`/`length.*` arm and no "it writes the heap" arm. Its doc
   comment no longer says "no input is keyed by an array variable" and no longer cites ADR 0015.
   It says which inputs inlining binds to the caller's own (`null.*`, `cast.*`, `length.*`: `In`,
   and nothing changes them) and which ones it threads through the call's heap pairs (`field.*`,
   `array.*`).
2. "it has a by-ref parameter" is raised only for a `Ref`/`Out` parameter whose name is not
   synthesised (`IrParameterNames.IsSynthesised`). A `ref %field.*` or `ref %array.*` parameter is
   not an obstacle.
3. A new obstacle, "a self-call's heap pairs do not match the heap parameters", is raised when some
   self-call lacks exactly one heap pair per synthesised `Ref` parameter, matched by map name.
   (P1-005 settles how a pair names its map. Use that rule and log it as a `Decision:` line.)
4. `IrEditor.InlineCall` binds each synthesised `Ref` parameter of the copy to the self-call's
   `before` version of that map. The join block defines each `after` with a phi over the copy's
   exits, and each operand is the version that exit's `IrReturn`/`IrThrow` `outs` names, renamed
   into the copy. `In` synthesised inputs stay bound to the caller's own, as today.
5. `IrUnrollerTests`: the two `an input is keyed by an array variable` rows of
   `SomeSelfRecursionCannotBeInlined` and the `it writes the heap` row of
   `InliningObstaclesLookAtTheBody` become rows of a new theory,
   `SelfRecursionOverTheHeapIsInlined`. Each row asserts a null obstacle, an `Unroll` that
   `IrValidator` accepts, and an `IrGen.Run` of the unrolled procedure whose outcome, trace and
   final heap equal the original's, for an input that stays within the bound. The rows are: a read
   of `array.*` and `length.*`, a read of `field.*`, a write of `field.*` before the self-call and a
   read after it, and a write inside the callee that the caller reads after the call. A `ref %n`
   row stays under "it has a by-ref parameter". A self-call with a missing heap pair and one with a
   duplicated heap pair are rows under criterion 3's message.
6. `Fixtures/loops/recursion-heap.ir`'s second comment line no longer says rung 1 cannot inline
   it. It stays `Equivalent(lockstep-induction)`: rung 1 now inlines it, but the input reaches the
   bound, so rung 1 cannot prove it. The new `LadderFixtureTests.RungOneInlinesAHeapSelfCall` asserts that
   its first ladder step's outcome is not `NotApplicable`.
7. A new fixture, `Fixtures/loops/recursion-array-divergent.ir`, is `Divergent(bounded)`: a
   self-recursive sum over `array.*`/`length.*`, whose new side changes the element read at the
   base case. It is listed in `LadderFixtureTests.Names`. Before this ticket, rung 1 said
   not-applicable for it and the verdict came from a later rung or was Unknown. Record which in
   Notes.

## Files
`src/Equiv.Core/Ir/IrUnroller.cs`; `tests/Equiv.Core.Tests/Ir/IrUnrollerTests.cs`;
`tests/Equiv.Verify.Z3.Tests/Fixtures/loops/recursion-heap.ir`;
`tests/Equiv.Verify.Z3.Tests/Fixtures/loops/recursion-array-divergent.ir`;
`tests/Equiv.Verify.Z3.Tests/LadderFixtureTests.cs`.

## Tests
`SomeSelfRecursionCannotBeInlined` (rows changed), `InliningObstaclesLookAtTheBody` (row moved),
`SelfRecursionOverTheHeapIsInlined`, `LadderFixtureTests.FixtureTakesTheVerdictPathItsFirstLineNames`
(new row), `LadderFixtureTests.RungOneInlinesAHeapSelfCall`.

## Size guard
One file in `src/`. If `IrCall`, the validator, `IrText` or the encoder needs a change, stop: that is
P1-005's shape being wrong, not this ticket's work.

## Out of scope
Inlining a self-call with a source-language `ref`/`out` parameter (M4-003 territory). Mutual
recursion (shared calls, ADR 0019). `length.*` changing at an allocation: P2-001 (`new T[n]`) must
recheck criterion 1's claim that nothing changes an `In` input, and add an obstacle if array creation
defines a new `length.*` version.

## Notes
- Decision: no new ADR and no ADR clarification. The ticket applies M3-002's rule (inline only when
  exact) to heap inputs that M3-002 did not have: `Ref` heap maps (M3-007), maps keyed by value
  (P1-006), and heap pairs on a call (P1-005). It changes no verdict's meaning, no Core contract and
  no component boundary. ADR 0015 does not mention inlining. `IrUnroller` cited it only as the
  source of per-variable array keys, and P1-006 has already removed those. Rule: equiv-adr bar test,
  row 3.
- Decision: scope is all three heap obstacles, not only the array-variable one it was filed about.
  Removing that arm alone unblocks only `length.*`-only procedures, because since M3-007 the by-ref
  arm refuses every `field.*`/`array.*` input first. Rule: equiv-decide, smallest change that
  delivers the Goal.
- It depends on P1-005 rather than landing between P1-006 and P1-005. Once P1-005 puts heap pairs on
  the self-call, the join must define their `after` versions. Before P1-005 there is nothing to bind,
  and an inliner written then would leave the `after` versions undefined as soon as P1-005 landed.
