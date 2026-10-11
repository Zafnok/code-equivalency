# P2-157 Soundness: a struct made with no constructor names a type argument in its interface list, and no text holds it
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-152

## Goal
P2-152 writes, after a reference to a constructor, the declaration of every type argument the
type's base types and interfaces are handed (ADR 0024, clarification of 2026-10-10 (P2-152)). A
struct can be made with no constructor:

    interface W { int Width(); }
    interface J<T> : W where T : unmanaged { int W.Width() => sizeof(T); }
    struct K : J<S> { }
    int M() => ((W)default(K)).Width();     // or: new K[1][0], or a field of type K never assigned

`M`'s text names `K` and `W` and no constructor, and `J<T>`'s text is the same for every `T`. A pair
that changes only `S`'s fields has equal fingerprints for `M`.

Found while working P2-152 by reading its rule; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarification of 2026-10-10 (P2-152), "What the text still does not hold"), ADR 0019,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Handed`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test on `BodyFingerprinter` shows `M` above with one fingerprint beside two declarations
   of `S`, or the Goal is struck with the test kept.
2. Decide through `.claude/skills/equiv-adr`'s bar test which text holds the declaration (a
   `default` of such a struct, a conversion of one to an interface, or none and the limit stays
   written down), and record which row applied in `## Notes`, with what it costs in congruent
   results on `gitextensions-8522`, counted.
3. The decision is implemented, and a test asserts that a `default` of a struct with no generic
   interface of the solution keeps the text it has.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, its tests, and the clarification
criterion 2 produces.

## Tests
In `BodyFingerprinterTests`: `ATypeArgumentInAStructsInterfaceListIsInTheTextOfABodyThatMakesOne`
and `ADefaultOfAStructWithNoGenericInterfaceKeepsItsText`.

## Size guard
One pull request.

## Out of scope
- The lowering (P2-154).
- A class, which is made only by a constructor (P2-152).

## Notes
- Filed 2026-10-10 from P2-152.
