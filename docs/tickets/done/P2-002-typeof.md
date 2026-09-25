# P2-002 `typeof(T)` is a shared synthesised input, not opaque
Status: done (PR #183)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-010

## Goal
M3-022's census found `TypeOf` in the top fifteen of every agent pair (6 bodies), and no ticket
owned it. Minimal repro:

```csharp
static bool IsText(object o) => o.GetType() == typeof(string);
static string Name() => typeof(Program).Name;
```

After this ticket, `typeof(T)` for a closed type `T` lowers to a read of a synthesised `In` input
`typeof.<T>`, of the `System.Type` sort, shared by both sides by name (ADR 0021 naming). It is
non-null and adds no trace event. An open generic `T` stays opaque.

## Spec references
VERIFICATION-MODEL section 2 (synthesised inputs); ADR 0021; `HeapInputs`; the `equiv-extend-ir`
skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `typeof(string)` on both sides reads the same input, unit-tested.
2. `typeof(T)` with `T` a method or type parameter stays `IrOpaque("TypeOf")`.
3. VERIFICATION-MODEL's synthesised-inputs paragraph and the coverage table list `typeof.<T>`.

## Size guard
One lowering arm plus one `HeapInputs` entry. Anything more, stop.

## Out of scope
Reflection members on the `Type` (they are ordinary property reads, M3-010).

## Notes

Decision: `typeof(T)` for a closed `T` is a plain scalar `In` input of `System.Type` sort read
directly (like `this`), not a map indexed by the operand (unlike `cast.<From>.<To>`), since there
is nothing to index by — one input per closed `T`. Its nullness is asserted provably-false in
`Nullness()` (alongside `IObjectCreationOperation`/`IInstanceReferenceOperation`), not
over-approximated through the `null.<Sort>` map as a cast conversion's result is, because the
ticket states it is non-null and .NET guarantees a `typeof` result is never null. Decided per
`equiv-decide`; no ADR needed (representation detail, not an architectural choice).
