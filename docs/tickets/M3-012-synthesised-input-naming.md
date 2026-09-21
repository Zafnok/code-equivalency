# M3-012 The synthesised-input naming rule is enforced, and `@this` no longer collides
Status: todo
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-001

## Goal
ADR 0021 makes the product encoding tell a C# parameter from a synthesised input by name: a
synthesised input is `this` or contains a dot, and a C# parameter never is. M3-001 relies on the
rule, but nothing checks that the frontend keeps it, and it already breaks once: an instance method
with a parameter named `@this` (Roslyn reports its name as `this`) lowers to two parameters both
named `this`, one the C# argument and one the receiver. The Debug build trips
`Debug.Assert(IrValidator.Validate(...))` in `IrLowerer`; a Release build emits the invalid IR
silently, and the encoder would share the argument by name instead of by position. Fix the
collision and pin the rule with a test.

## Spec references
ADR 0021; VERIFICATION-MODEL.md section 2 (synthesised inputs); ADR 0018 (heap parameters).

## Acceptance criteria (all must hold; nothing beyond them)
1. `class C { int f; int M(int @this) => f + @this; }` lowers to IR that validates, whose C#
   parameter is not a synthesised name, and whose receiver input is still `this`. `Z3Backend`
   gives Equivalent for it against the same method with the parameter renamed (`int M(int v) => f + v;`).
2. The predicate "is this parameter name synthesised" has one definition. If the frontend's
   test needs it, it moves from `Equiv.Verify.Z3.ProductEncoder.IsSynthesised` to `Equiv.Core`
   (next to the IR types) and the encoder calls it there; no copy lives in a test.
3. A frontend test lowers every method of every `samples/` pair (both sides) and asserts, for
   every procedure: every C# parameter is not synthesised, every synthesised input is, and all
   C# parameters come before all synthesised inputs.
4. VERIFICATION-MODEL section 2 states how a C# parameter whose name would collide is spelled
   in IR.

## Files
- `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (`Signature`), or `HeapInputs.cs` if the
  receiver is what changes
- `src/Equiv.Verify.Z3/ProductEncoder.cs` and one `src/Equiv.Core/Ir/` file, only if criterion 2
  moves the predicate
- `tests/Equiv.Frontend.CSharp.Tests/Lowering/` (the collision and the samples-wide rule)
- `docs/VERIFICATION-MODEL.md`

## Tests
- Unit: the `@this` method from criterion 1 validates and keeps the two inputs distinct.
- Unit: the renamed-pair Equivalent from criterion 1.
- Integration-style unit over `samples/`: criterion 3.

## Size guard
Five files. This is a naming fix plus one test. If it is touching the encoder's pairing logic,
it has drifted: ADR 0021 already decided that.

## Out of scope
- Replacing the naming rule with an explicit flag on `IrParameter` (rejected in ADR 0021).
- Turning `IrLowerer`'s `Debug.Assert` on the validator into a Release-mode check. Worth doing,
  but it changes what a lowering bug does to a run, which is ADR 0022's question.
- A Java frontend.

## Notes
