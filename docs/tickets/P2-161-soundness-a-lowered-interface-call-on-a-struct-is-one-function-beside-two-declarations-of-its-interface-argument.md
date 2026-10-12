# P2-161 Soundness: a lowered body that names a struct is the same beside two declarations of a type the struct's interface list hands over
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-154, P2-157

## Goal
P2-157 writes, after an operation that names a struct, the declaration of every type argument the
struct's interface list hands a generic interface of the solution (ADR 0024, clarification of
2026-10-12 (P2-157)), so a pair that changes only that type is no longer congruent:

    interface W { int Width(); }
    interface J<T> : W where T : unmanaged { int W.Width() => sizeof(T); }
    struct K : J<S> { }
    int M(int x) => ((W)default(K)).Width() + x;      // legacy
    int M(int x) => x + ((W)default(K)).Width();      // modern, beside another declaration of S

Such a pair is then lowered and goes to the solver. The lowered body has a default value of `K`, a
conversion and a call to `W::Width`, none of which names `S`, so they are the same on both sides,
and `J<T>`'s own pair is unchanged. This is P2-154's Goal for the rule P2-157 added:
`Layouts.HandsOver` asks `Layouts.TypeArguments`, and not `Layouts.Implemented`.

Found while working P2-157 by reading the lowering; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarifications of 2026-10-10 (P2-154) and 2026-10-12 (P2-157)), ADR 0019,
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`Implemented`, `HandsOver`),
`tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A case in `LayoutEquivalenceTests` shows the pair above Equivalent from the solver, or the Goal
   is struck with the test kept. The same for `Make<K>().Width()`, where
   `static W Make<T>() where T : struct, W => default(T)`.
2. Decide through `.claude/skills/equiv-adr`'s bar test what a lowered body does with an operation
   `Layouts.Implemented` gives a type with a layout (`Layouts.IsLaidOut`), and record which row
   applied in `## Notes`, with what it costs on `gitextensions-8522` in compare mode quick, one run
   on each side of the change, counted by rule id.
3. The decision is implemented, the lowering reads the rule the fingerprint reads, and
   `docs/tickets/IOPERATION-COVERAGE.md` says so in the rows it changes. An operation that names a
   struct with no generic interface of the solution is lowered as it was; a test asserts it.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, their tests,
`tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`, `docs/tickets/IOPERATION-COVERAGE.md`,
and the clarification criterion 2 produces.

## Tests
In `LayoutEquivalenceTests`: the cases criterion 1 names. In `IrLowererTests`: one for the decision
and one for criterion 3's second sentence.

## Size guard
One pull request after the decision. If the measured cost in criterion 2 is more than a few pairs,
stop after the measurement and write it up before choosing.

## Out of scope
- The text (P2-157).
- A class handed to a generic member that creates it (P2-162).

## Notes
- Filed 2026-10-12 from P2-157.
