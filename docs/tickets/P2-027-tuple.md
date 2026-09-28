# P2-027 Tuple literals and `.Item1`/named tuple fields have no lowering
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `Tuple` in Git Extensions' opaque reasons (54 occurrences legacy
and modern), and no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static int SumPair((int X, int Y) p) => p.X + p.Y;
static (int, int) MakePair(int a, int b) => (a, b);
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `Tuple` row yet); `PropertyReference`'s row (an
auto-property backed by a field is the closest existing precedent for a tuple element read); the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, the IR representation for a value-tuple: most likely a fixed-arity
   record-like sort with one field per element (`.Item1`/`.Item2`/named tuple field access all
   read the same underlying element by position, since names are compile-time only). Add the
   `IOPERATION-COVERAGE.md` row either way (lowered or opaque).
2. Snapshot tests for a tuple literal, a tuple parameter's element read by position, and the same
   read through a named tuple field.
3. `==`/`!=` on tuples (element-wise) can stay opaque with reason `Tuple` if in scope creep;
   record that decision in Notes rather than expanding this ticket to cover it.

## Size guard
2- and 3-element tuples of primitive types only. A tuple containing a reference type, a nested
tuple, or arity above 3 can stay opaque; note that in `IOPERATION-COVERAGE.md`'s row rather than
handling it here.

## Out of scope
Tuple deconstruction (P2-025), `ValueTuple` used explicitly by type name instead of tuple syntax.

## Notes
