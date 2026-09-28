# P2-029 A call through `dynamic` (`DynamicInvocation`) has no lowering
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `DynamicInvocation` in Git Extensions' opaque reasons (1
occurrence, `Microsoft.Validates` — a Code Contracts style helper called through `dynamic`), and
no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static object CallIt(dynamic d) => d.DoSomething();
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `DynamicInvocation` row yet); `Conversion`'s row (a
conversion involving `dynamic` already stays opaque there, so this is consistent); the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, that this stays opaque with reason `DynamicInvocation` (the call
   target is resolved by the DLR at runtime, not statically known, so there is no callee identity
   to call) — write the `IOPERATION-COVERAGE.md` row confirming this is `opaque` by design, not
   merely "not yet lowered".
2. A test confirming the repro is opaque with reason `DynamicInvocation`.

## Size guard
Documentation-and-test only; this is not expected to ever lower.

## Out of scope
Any attempt to model DLR call-site binding.

## Notes
