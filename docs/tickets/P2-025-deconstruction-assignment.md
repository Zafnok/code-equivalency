# P2-025 `(a, b) = ...` (`DeconstructionAssignment`) has no lowering
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `DeconstructionAssignment` in Git Extensions' opaque reasons (80
occurrences legacy and modern — the single largest uncovered reason found by this run, ahead of
`Tuple` at 54), and no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static int SumSwap(int a, int b) { (a, b) = (b, a); return a + b; }
static void Deconstruct(this (int X, int Y) p, out int x, out int y) { x = p.X; y = p.Y; }
static int SumOut((int, int) p) { (int x, int y) = p; return x + y; }
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `DeconstructionAssignment` row yet); ADR 0018 (call
trace / evaluation order); the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, the lowering for a deconstruction into existing locals/fields
   (evaluate the right side once, assign left-to-right, same as `SimpleAssignment`'s existing
   evaluation-order rule) versus a `Deconstruct` method call; either lowers or stays opaque with
   reason `DeconstructionAssignment`, and either way add the `IOPERATION-COVERAGE.md` row.
2. Snapshot tests for the tuple-literal repro and the `Deconstruct`-call repro.
3. 80 occurrences on one real repo is high enough that this ticket's own effect on the lowerable
   share should be visible in the next corpus run's `docs/runs/` numbers; note the before/after in
   this ticket's Notes when done.

## Size guard
Deconstruction into existing variables/fields, one level, no nested tuple patterns. A `var (a, b)`
declaration or nested deconstruction is a separate ticket if this one's Size guard trips on it.

## Out of scope
Deconstruction in a `foreach` (already partly M4-001's territory) beyond what the repro needs;
nested/recursive tuple patterns.

## Notes
