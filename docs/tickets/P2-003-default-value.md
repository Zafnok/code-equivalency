# P2-003 `default(T)` is lowered, not opaque
Status: todo
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M3-022's census found `DefaultValue` in ServiceAnt's top fifteen, and no ticket owned it. Roslyn
folds `default` to a constant for most types. What stays opaque is a `default` whose value is not a
compile-time constant. Minimal repro:

```csharp
static T OrDefault<T>(bool has, T value) => has ? value : default;
static Point Origin() => default(Point);   // Point is a user struct
```

After this ticket, `default` of a reference type or of a type parameter lowers to the null value
of its sort, with `null.<T>` set, and `default` of a struct lowers to a fresh value whose field maps
read as each field's default.

## Spec references
VERIFICATION-MODEL section 2 (null shadow, field maps); ADR 0014; `docs/tickets/IOPERATION-COVERAGE.md`
row `DefaultValue`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Both repro methods lower with no `IrOpaque`, snapshot-tested.
2. A type parameter's `default` is null when the parameter is constrained to `class`, and opaque
   otherwise.
3. The coverage-table row names the tests.

## Size guard
If a struct default needs new field-map machinery, lower only the reference-type case, keep the
struct case opaque and log a `Deviation:` line.

## Out of scope
`default` literals in pattern matching.

## Notes
