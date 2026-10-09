# P2-150 Soundness: the lowered body of an operation that reads a type's layout holds none of it
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-149

## Goal
P2-149 put the declaration of a type into the text of a body that reads its layout, so a pair whose
declarations differ is no longer congruent (ADR 0024, clarification of 2026-10-09 (P2-149)). Such a
pair is then lowered and goes to the solver, and the IR holds no layout. Two shapes are lowered as
if the layout did not exist:

- a field of a `[StructLayout(LayoutKind.Explicit)]` type is its own `field.<Type>.<Field>` map
  (`docs/tickets/IOPERATION-COVERAGE.md`, `FieldReference`). Two fields at one `[FieldOffset]` are
  one storage location, so `s.A = 1; return s.B;` and `int b = s.B; s.A = 1; return b;` differ and
  the model says they are equal. This holds on a pair whose two declarations are the same;
- a call that is handed a type and reads its layout (`Marshal.SizeOf<S>()`, `Unsafe.SizeOf<S>()`,
  `MemoryMarshal.Cast<byte, S>(...)`) is an `IrCall` both sides share by its identity, which names
  `S` and nothing of its declaration. On a pair whose declarations of `S` differ and whose bodies
  differ elsewhere, the two calls are one function.

The other operations of P2-149's rule (`sizeof` of a user-defined struct, a pointer, a call through
a function pointer, an inline array) are believed to be opaque fragments, whose sharing the
fingerprint now decides. `sizeof` is tested; the rest is read off the coverage table.

Found while working P2-149 by reading the coverage table; none was reproduced as a wrong result.
The first step is the repro.

## Spec references
ADR 0024 (the clarification of 2026-10-09 (P2-149), "The solver path needs the same fact"), ADR 0025
(a side-specific function), ADR 0014 (an opaque), `docs/tickets/IOPERATION-COVERAGE.md`
(`FieldReference`, `Invocation`, `SizeOf`), `.claude/skills/equiv-extend-ir`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Layout`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Each bullet of the Goal is a test that shows an Equivalent the solver should not give, or the
   bullet is struck with the test kept. For a pointer indirection, a call through a function
   pointer and an inline-array access, a test shows the operation is an opaque whose fingerprint
   differs when the declaration does, or the operation joins the Goal.
2. Before any change to the lowering: decide through `.claude/skills/equiv-adr`'s bar test what a
   lowered body does with each (an opaque with its own reason, a model of the shared storage, a
   side-specific function when the declarations differ), and record which row applied in `## Notes`.
3. The decision is implemented, and `docs/tickets/IOPERATION-COVERAGE.md` says so in the rows it
   changes. A field of a type with no explicit layout lowers as it does today; a test asserts it.
4. The public-corpus results that change are counted in `## Notes`, by rule id before and after,
   from one run of `powershell-19687` in compare mode quick on each side of the change.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`, and the
ADR or clarification criterion 2 produces.

## Tests
In `IrLowererTests`: one per bullet of the Goal and one per operation criterion 1 names, and
`AFieldOfATypeWithoutAnExplicitLayoutIsStillItsMap`.

## Size guard
One pull request after the decision. If criterion 2 decides a model of shared storage, stop after
the opaque and file the model.

## Out of scope
- The fingerprint (P2-149).
- A type from a reference.
- A generic member of the solution that reads its type parameter's layout (P2-151).

## Notes
- Filed 2026-10-09 from P2-149.
