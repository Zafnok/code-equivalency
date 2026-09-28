# P2-028 `new T()` on a generic type parameter (`TypeParameterObjectCreation`) has no lowering
Status: done (PR #237)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `TypeParameterObjectCreation` in Git Extensions' opaque reasons (2
occurrences), and no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static T MakeNew<T>() where T : new() => new T();
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `TypeParameterObjectCreation` row yet); `ObjectCreation`
and `TypeOf`'s rows (both already special-case a type parameter as opaque, so this reason is the
object-creation analogue of that existing pattern); the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, whether this stays permanently opaque (like an unconstrained
   `default(T)`, P2-003) since the concrete type is only known at the generic instantiation site
   equiv does not specialise over, or whether there is a cheaper partial answer; add the
   `IOPERATION-COVERAGE.md` row either way.
2. A test confirming the repro is opaque with reason `TypeParameterObjectCreation` (or lowered, if
   `equiv-decide` finds a lowering).

## Size guard
If this needs generic instantiation/specialisation machinery, stop — that is out of scope for an
S ticket, keep it opaque and write an ADR only if a later ticket needs to lower it.

## Out of scope
Generic method/type specialisation in general.

## Notes
- Decision: stays permanently opaque with reason `TypeParameterObjectCreation`. The constructor `new T()` runs is chosen at each instantiation site, so the only partial lowering would be an uninterpreted call keyed on the type parameter, whose identity differs across the pre/post methods whenever a type parameter is renamed or reordered, and pairing those is the generic specialisation the Size guard rules out. It matches the existing type-parameter cases of `TypeOf` and unconstrained `DefaultValue` (P2-003). Only 2 occurrences in Git Extensions (M4-007).
- The repro was already a case of `IrLowererTests.UnsupportedConstructIsOpaqueWithItsName` (added in M2-004) and passes as is: the lowerer's fallback arm names the unhandled `OperationKind`, so no `src/` change was needed; this ticket adds the coverage row and records the decision.
