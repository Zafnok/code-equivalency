# P2-152 Soundness: a type's base list hands a generic type of the solution a type argument, and no text holds it
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-151

## Goal
P2-151 writes the declaration of a type after a reference to a generic member of the solution that
is handed it (ADR 0024, clarification of 2026-10-09 (P2-151)). The body that names the type argument
holds it. A type argument can also be named where there is no body:

    class Box<T> where T : unmanaged { public virtual int Size() => sizeof(T); }
    class D : Box<S> { }
    int M(D d) => d.Run();          // Run is declared in D, or the call goes through an interface

`new D()` references `D`'s constructor, whose type has no type argument, and a call to a member `D`
declares or to an interface member names nothing of `Box<S>`. `Box<T>.Size`'s text is the same for
every `T`. A pair that changes only `S`'s fields has equal fingerprints for every body here that
does not itself reference a member through `Box<S>`.

Found while working P2-151 by reading its rule; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarification of 2026-10-09 (P2-151), "What the text still does not hold"), ADR 0019,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Handed`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test on `BodyFingerprinter` shows a body that creates a `D`, and one that calls a member
   `D` inherits through an interface, each with one fingerprint beside two declarations of `S`, or
   the Goal is struck with the test kept.
2. Decide through `.claude/skills/equiv-adr`'s bar test which text holds the declaration (a
   reference to any member of a type whose base types or interfaces are handed one, an object
   creation only, or none and the limit stays written down), and record which row applied in
   `## Notes`, with what it costs in congruent results on `gitextensions-8522`, counted.
3. The decision is implemented, and a test asserts that a reference to a member of a type with no
   generic base type of the solution keeps the text it has.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, its tests, and the clarification
criterion 2 produces.

## Tests
In `BodyFingerprinterTests`: `ATypeArgumentInABaseListIsInTheTextOfABodyThatUsesTheType` and
`AMemberOfATypeWithNoGenericBaseKeepsItsText`.

## Size guard
One pull request.

## Out of scope
- The lowering (P2-150).
- A generic type of the solution handed to a member of a reference that runs its code by reflection.

## Notes
- Filed 2026-10-09 from P2-151.
