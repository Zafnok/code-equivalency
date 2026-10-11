# P2-103 `x as T` followed by a null check equals `x is T t`
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-058

## Goal
P2-058's cleanup run on `powershell-19687` (IDE0019) reported 5 EQ002, all false positives, and
proved none of the 12 edited pairs. Every edit turns `T t = x as T; if (t != null)` into
`if (x is T t)`. M4-005 lowers the `is` form as a read of `istype.<From>.<T>` and the `as` form as
`cast.<From>.<T>` with an over-approximated null flag that "does not read `istype`". So the solver
is free to make the cast succeed where the type test fails. Tie the two: for reference types,
`x as T` is null exactly when `x` is null or `istype.<From>.<T>` is false at `x`.

## Spec references
`docs/tickets/done/M4-005-type-tests-and-downcasts.md`; `docs/tickets/IOPERATION-COVERAGE.md` rows
`Conversion`, `IsType`, `DeclarationPattern`; `.claude/skills/equiv-extend-ir/SKILL.md`;
`docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A sample pair with `as` plus `!= null` on one side and an `is` declaration pattern on the other,
   branches unchanged, is Equivalent with a `proofMethod` other than `congruence`.
2. A pair where the `is` side tests a different type than the `as` side stays Divergent or Unknown.
3. Unboxing, generic and user-defined conversions keep the lowering they have now.
4. Rerun `powershell-19687` with the "Cleanup pairs" part of `equiv-corpus-run`. Record in Notes how
   many of its 5 EQ002 and 7 Unknown edited pairs change verdict. None may stay EQ002.
5. The differential soundness gate passes on the PR budget.

## Tests
- `IrLowererTests.AnAsCastIsNullExactlyWhenTheTypeTestFails`
- `EquivalenceTests.AsPlusNullCheckEqualsAnIsPattern`
- `EquivalenceTests.AsAndIsOnDifferentTypesStayDifferent`
- a sample under `samples/` beside P2-049's cleanup samples, if that ticket is done

## Size guard
A new IR instruction, or a change to how `istype` itself is modelled: stop and apply `equiv-adr`.

## Out of scope
Value-type and nullable-value `as`. Pattern forms other than the declaration and type patterns
(P2-104 owns `is not`).

## Notes
- Observation: the Goal names the wrong half. M4-005 already records `x as T`'s null flag as the negation of its type
  test, and a pair whose null check is on a `string` was already Equivalent. The five false EQ002 came from the null
  check: `t != null` on a class with no `==` of its own compares as `object`, so Roslyn wraps `t` in an implicit
  reference conversion, and M3-010 read that conversion's nullness from `null.System.Object` at the converted value,
  not from `t`. One side tested a free flag, the other the type test.
- Decision: an implicit reference conversion's result is null exactly when its operand is. `Cast` records the operand
  it lowered, and `Nullness` goes through such a conversion to the operand's own nullness (a variable's shadow, an
  `as`'s failed test, else `null.<From>` at the operand). That is exact for every reference conversion, so it is not
  narrowed to the `as` case. Boxing keeps M3-010's reading (`null.<To>`), and so does a new `Collection<T>` converted
  to an interface (ADR 0043), whose operand is never lowered. No new IR instruction and `istype` is modelled as before,
  so the size guard does not trip.
- Decision: the sample is a new method, `Tidy.RadiusOf`, in `samples/cleanup-modern-syntax` (P2-049), not a new sample
  directory: Equivalent, `proofMethod: bounded`.
- Observation: five IR snapshots change the same way (`UsingDeclaration`, `ForEachOverIEnumerableOfInt`,
  `ForEachWithBreak`, `QueryExpression`, `AsyncControllerAction`): the `finally`'s null test of the resource or
  enumerator converted to `IDisposable` now reads the resource's own shadow, and `null.System.IDisposable` is no
  longer an input. `IlLoweringParityTests` loses five known differences for the same reason (`business-layer`
  `Export` and `Reserve` on both sides, `cleanup-modern-syntax` legacy `Positives`): the IL lowering always read the
  reference's own shadow, and the two lowerings now agree there.
- Criterion 4: `powershell-19687` rerun at feee17e1 plus this change, compare mode quick (the rule in
  `equiv-corpus-run` section 1: a run that checks whether named pairs' results changed), 24 threads, 190 s, exit 0,
  no project skipped. Whole run: EQ001 34155, EQ002 0, EQ003 28. Of the 5 EQ002 edited pairs, all 5 are now Equivalent
  (`proofMethod: bounded`): `IsClassNameQuerySet`, `NewSubscriptionResultHandler`, `CimIndicationHandler`,
  `ErrorRecordFromAnyException`, `WriteObject(object,XOperationContextBase)`. Of the 7 Unknown edited pairs, 1 is now
  Equivalent (`CimMethodResultObserver::OnNext`, `lockstep-induction`, was `timeout`) and 6 stay Unknown:
  `GetBaseObject` (`recursion`), `GetReferenceOrReferenceArrayObject` (`opaque`, method scope; was `abstraction`),
  `CreateQuery` and `CreateCimInstance(string,string,...)` (`unaligned-loop`), `GetCimInstance` (`opaque`, line scope),
  `BuildSessionOptions` (`timeout`). None is EQ002. Other tickets landed between the 2026-10-02 run and this one, so
  the Unknown changes are not all this ticket's: the last earlier run on this pair (2026-10-08, quick) had 6 EQ002,
  these five and `CreateQuery`, and `OnNext` still at `timeout`.
- Observation: the run used the pair as another worktree had already fetched and prepared it (`tools/corpus/README.md`'s
  three steps for this pair); nothing was fetched or edited. No `docs/runs/` file was added, so the README's
  scoreboard is untouched.
