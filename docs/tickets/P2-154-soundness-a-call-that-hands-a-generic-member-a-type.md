# P2-154 Soundness: a lowered call that hands a generic member of the solution a type is one function beside two declarations of the type
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-150, P2-151

## Goal
P2-151 writes, after a reference to a generic member of the solution, the declaration of every type
it hands the member (ADR 0024, clarification of 2026-10-09 (P2-151)), so a pair that changes only
the type is no longer congruent:

    static int Size<T>() where T : unmanaged => sizeof(T);
    int M(int x) => Size<S>() + x;      // legacy
    int M(int x) => x + Size<S>();      // modern, beside another declaration of S

Such a pair is then lowered and goes to the solver. The call is an `IrCall` whose identity names
`Size<N.S>` and nothing of `S`'s declaration, so it is one function for both sides, and `Size`'s
own pair is unchanged (ADR 0019 does not catch it: a type has no pair). This is the second bullet of
P2-150's Goal for a callee P2-150's rule does not reach: `Size` is not under
`System.Runtime.InteropServices` and takes no pointer, so `Layouts.IsHanded` is false for it.

P2-150 and P2-151 were written side by side and each left this to the other. Found while merging
them; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarifications of 2026-10-09 (P2-150) and (P2-151)), ADR 0019, ADR 0014,
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`IsHanded`),
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Handed`),
`tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A case in `LayoutEquivalenceTests` shows the pair above Equivalent from the solver, or the Goal
   is struck with the test kept. The same for a member of a generic type of the solution
   (`Box<S>.Size()`).
2. Before any change to the lowering: decide through `.claude/skills/equiv-adr`'s bar test what a
   lowered body does with such a call, and record which row applied in `## Notes`. P2-151 measured
   what its rule costs congruence on `gitextensions-8522`; the decision says what the same rule
   would cost the lowering if every such call became an opaque, from one run of that pair in
   compare mode quick on each side of the change, counted by rule id.
3. The decision is implemented, the rule lives in one place that the fingerprint and the lowering
   both read, and `docs/tickets/IOPERATION-COVERAGE.md` says so in the rows it changes. A call that
   hands a generic member of the solution only types from a reference or type parameters is the
   call it was; a test asserts it.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, `src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`,
their tests, `tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`,
`docs/tickets/IOPERATION-COVERAGE.md`, and the ADR or clarification criterion 2 produces.

## Tests
In `LayoutEquivalenceTests`: the cases criterion 1 names. In `IrLowererTests`: one for the decision
and `ACallThatHandsAGenericMemberNoDeclaredTypeIsStillACall`.

## Size guard
One pull request after the decision. If the measured cost in criterion 2 is more than a few pairs,
stop after the measurement and write it up before choosing.

## Out of scope
- A type argument named in a base list (P2-152).
- An auto-property or an event of an explicit layout (P2-153).
- A type from a reference.

## Notes
- Filed 2026-10-09 from P2-150, at the merge with P2-151.
