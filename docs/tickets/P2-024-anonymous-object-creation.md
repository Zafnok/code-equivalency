# P2-024 `new { ... }` (`AnonymousObjectCreation`) has no lowering
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `AnonymousObjectCreation` in Git Extensions' opaque reasons (6
occurrences), and no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static int SumXY(int x, int y) { var p = new { X = x, Y = y }; return p.X + p.Y; }
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `AnonymousObjectCreation` row yet); the
`equiv-extend-ir` skill; the `Tuple` row this ticket's sibling P2-027 adds (an anonymous type is
structurally close to a tuple: a fixed set of named readonly properties).

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, whether an anonymous object's properties lower like a tuple's
   elements (P2-027) or stay opaque with reason `AnonymousObjectCreation`; add the
   `IOPERATION-COVERAGE.md` row either way.
2. A snapshot test for the repro.

## Size guard
One `IOperation` kind, reusing whatever representation P2-027 picks for `Tuple` if that lands
first. If it needs a new record-like IR sort, stop and write an ADR instead.

## Out of scope
Anonymous types with computed (non-simple-member-access) property values beyond what the repro
needs; `with` expressions on anonymous types.

## Notes
