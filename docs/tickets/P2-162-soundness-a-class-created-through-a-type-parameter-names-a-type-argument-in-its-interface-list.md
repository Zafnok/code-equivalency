# P2-162 Soundness: a class created through a type parameter names a type argument in its interface list, and no text holds it
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-157

## Goal
P2-152 holds what a class's base list hands over in the text of a body that references the class's
constructor (ADR 0024, clarification of 2026-10-10 (P2-152)), and P2-157 leaves a class to that
rule. A class can be created with no reference to its constructor, by a generic member that is
handed it:

    interface W { int Width(); }
    interface J<T> : W where T : unmanaged { int W.Width() => sizeof(T); }
    class K : J<S> { }
    static W Make<T>() where T : W, new() => new T();
    int M() => Make<K>().Width();

`M`'s text has `K`'s declaration, since `Make` is handed `K` (P2-151). That declaration holds `K`'s
base type and its fields, and not its interfaces, so it does not reach `S`. `Make<T>`'s text is the
same for every `T`. A pair that changes only `S`'s fields has equal fingerprints for `M`.

A base type is reached: for `class D : Box<S>`, `Make<D>()` writes `D`'s declaration, which names
`Box<S>`, which hands over `S`. Only the interface list is missed.

Found while working P2-157 by reading its rule; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarifications of 2026-10-09 (P2-151), 2026-10-10 (P2-152) and 2026-10-12 (P2-157)),
ADR 0019, `src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`TypeArguments`, `Implemented`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test on `BodyFingerprinter` shows `M` above with one fingerprint beside two declarations
   of `S`, or the Goal is struck with the test kept.
2. Decide through `.claude/skills/equiv-adr`'s bar test which text holds the declaration (a
   reference that hands a member of the solution a class also hands over what the class's interface
   list does, or none and the limit stays written down), and record which row applied in
   `## Notes`, with what it costs in congruent results on `gitextensions-8522`, counted.
3. The decision is implemented, and a test asserts that a body that is handed an object of such a
   class and hands it to no generic member keeps the text it has.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs`, its tests, and the clarification criterion 2
produces.

## Tests
In `BodyFingerprinterTests`: `ATypeArgumentInAClasssInterfaceListIsInTheTextOfABodyThatHandsTheClassOver`
and `ABodyThatIsHandedAnObjectOfSuchAClassKeepsItsText`.

## Size guard
One pull request.

## Out of scope
- The lowering (P2-161).
- A struct (P2-157).

## Notes
- Filed 2026-10-12 from P2-157.
