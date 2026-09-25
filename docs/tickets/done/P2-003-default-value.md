# P2-003 `default(T)` is lowered, not opaque
Status: done (PR #193)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M3-022's census found `DefaultValue` in ServiceAnt's top fifteen, and no ticket owned it. Roslyn
folds `default` to a constant for most types. What stays opaque is a `default` whose value is not a
compile-time constant. Minimal repro:

```csharp
static T OrDefault<T>(bool has, T value) where T : class => has ? value : default;
static Point Origin() => default(Point);   // Point is a user struct
```

After this ticket, `default` of a reference type or of a `class`-constrained type parameter lowers
to the null value of its sort, with `null.<T>` set; `default` of a struct stays opaque (Deviation
below).

## Spec references
VERIFICATION-MODEL section 2 (null shadow, field maps); ADR 0014; `docs/tickets/IOPERATION-COVERAGE.md`
row `DefaultValue`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. The type-parameter repro (constrained to `class`) lowers with no `IrOpaque`, snapshot-tested;
   the struct repro stays opaque per the Size guard (Deviation below).
2. A type parameter's `default` is null when the parameter is constrained to `class`, and opaque
   otherwise.
3. The coverage-table row names the tests.

## Size guard
If a struct default needs new field-map machinery, lower only the reference-type case, keep the
struct case opaque and log a `Deviation:` line.

## Out of scope
`default` literals in pattern matching.

## Notes

Decision: `default` of a closed reference type, and of a `class`-constrained type parameter, was
already lowered correctly before this ticket: Roslyn's operation model sets `ConstantValue` to
`null` for both, so `IrLowerer.Lower`'s existing top-of-method `ConstantValue.HasValue` shortcut
(the same path `default(string)` and `default(int)` already took) picks it up as the designated
null element of its sort, and the existing null-shadow tracking (`Shadowed`, `Nullness`) already
threads `null.<Sort>` through it whenever it is stored into a variable. No dedicated
`IDefaultValueOperation` lowering arm was needed. This was confirmed by lowering
`static T M<T>() where T : class => default(T);` and the ternary repro both before and after this
PR's test additions: both already produced zero `IrOpaque` nodes and the correct shadow, unchanged.
An unconstrained or `struct`-constrained type parameter's `default`, and a non-generic struct's,
are not a Roslyn compile-time constant and already fell into the generic
`default: return Opaque(operation, operation.Kind.ToString(), context)` arm with reason
`DefaultValue`, which already matches acceptance criterion 2's "opaque otherwise" and needed no
change either. So this ticket's `src/` diff is empty; its diff is the pinning tests (unit,
snapshot) and the coverage-table row that were missing, per `equiv-decide` (no ADR: this is
confirming existing behaviour, not choosing a new representation).

Deviation (`equiv-adr` bar test row 2 — ticket text corrected above, flagged in the PR): the
Goal's original repro used an unconstrained `OrDefault<T>`, but acceptance criterion 2 requires an
unconstrained type parameter's `default` to stay opaque, so that repro could never satisfy the
original acceptance criterion 1's "no `IrOpaque`" for both methods as literally written. Corrected
the repro to `where T : class`, which is what the "reference type or type parameter" half of the
Goal actually describes. Separately, the struct repro (`default(Point)`) invokes the ticket's own
Size guard: a struct `default` that is a fresh value whose field maps read as each field's default
needs new encoder machinery (asserting per-field defaults at a receiver with no prior field-map
entry), which does not exist and is out of scope for this S-sized ticket. It stays opaque with
reason `DefaultValue`, pinned by `UnsupportedConstructIsOpaqueWithItsName`, same as an unconstrained
or `struct`-constrained type parameter's `default`. Needs your decision: whether a follow-up ticket
should add the struct field-map machinery, or whether struct defaults stay opaque indefinitely
(they are rare relative to the census's ServiceAnt finding, which was dominated by the
type-parameter case).
