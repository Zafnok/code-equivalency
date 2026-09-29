# P2-025 `(a, b) = ...` (`DeconstructionAssignment`) has no lowering
Status: done (PR #242)
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
- Decision: which deconstructions lower -> a statement whose value is a tuple literal (seen through its identity or tuple
  literal conversion) into locals, parameters, captured lvalues, fields and discards; a `Deconstruct` method call, a
  tuple-typed value, a nested tuple, a property or array element target and a used value stay opaque with reason
  `DeconstructionAssignment`. Alternatives: also lower `Deconstruct` as a call with `out` arguments (IOperation does not
  expose the method; it needs `SemanticModel.GetDeconstructionInfo` and a hand-built call shape), lower tuple-typed values
  (no IR for a tuple value; P2-027 owns `Tuple`). Rule: 4.
- Decision: evaluation order -> each field's receiver, then every element, then every null shadow, then each store, left to
  right, as `SimpleAssignment` does (P2-017) and as C# specifies. Reading every shadow before any store keeps a swap of
  references swapping their nullness. Alternatives: store as each element is read (wrong for a swap). Rule: 1.
- Decision: `var (a, b) = ...` and `(int a, int b) = ...` targets -> lowered, since they are the same lowering once the
  `DeclarationExpression` is unwrapped (one helper, no Size guard trip). Alternatives: leave them for a separate ticket.
  Rule: 4 (smaller than special-casing them opaque).
- The ticket's `Deconstruct`-call repro is an extension method on a tuple type, which the compiler never calls: a tuple
  value deconstructs natively. The snapshot uses a struct with an instance `Deconstruct` instead, so it is a real call.
- Roslyn quirk: a tuple literal whose element types already equal the targets' (the swap) is wrapped in an *identity*
  conversion, not a tuple literal one; one that converts an element (`(long, int) = (a, 3)`, or an explicit
  `((long, long))(a, b)`) is a tuple literal conversion with the element conversions inside the literal. Both are unwrapped.
- A record named `Deconstruct` in the oracle generator does not compile (CS0542: records generate a `Deconstruct`
  member), hence `Deconstruction`.
- Corpus before/after (criterion 3): before, M4-007 (`docs/runs/2026-09-27-m4-007-gitextensions-8522`) lists
  `DeconstructionAssignment` 80 legacy / 80 modern opaque sites, the largest unowned reason. After: not measured in this
  PR; a Git Extensions run takes 2h37m+ and the criterion names the next corpus run. What remains opaque there after this
  ticket is the `Deconstruct`-method, tuple-valued and property/array-target share, which the next run's reason table
  shows under the same reason.
