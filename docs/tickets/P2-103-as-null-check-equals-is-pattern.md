# P2-103 `x as T` followed by a null check equals `x is T t`
Status: todo
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
