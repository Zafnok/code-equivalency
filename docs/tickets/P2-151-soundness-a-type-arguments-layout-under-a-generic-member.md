# P2-151 Soundness: a type argument's layout reaches a generic member of the solution that reads it, and neither text holds it
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-149

## Goal
P2-149 writes a type's declaration after an operation that reads its layout (ADR 0024,
clarification of 2026-10-09 (P2-149)). A generic member of the solution can do such an operation on
its type parameter:

    static int Size<T>() where T : unmanaged => sizeof(T);
    int M() => Size<S>();

`Size`'s text holds `sizeof` of a type parameter, which has no declaration, so it is the same text
whatever `S` is. `M`'s text names `Size<N.S>` and nothing of `S`: the callee is not under
`System.Runtime.InteropServices` and takes no pointer. A pair that changes only `S`'s fields or its
`[StructLayout]` has equal fingerprints for both members and both are Equivalent by congruence,
although `M` returns another number. ADR 0019 does not cover it: `Size`'s own pair is unchanged,
and a type has no pair.

Found while working P2-149 by reading its rule; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarification of 2026-10-09 (P2-149), "What the text still does not hold"), ADR 0019,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Layout`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test on `BodyFingerprinter` shows `M` above with one fingerprint beside two declarations
   of `S`, or the Goal is struck with the test kept.
2. Decide through `.claude/skills/equiv-adr`'s bar test which text holds the declaration, and
   record which row applied in `## Notes`. The decision says, at least: whether it is the caller's
   text for every type argument it hands a generic member of the solution, or only for a member
   whose body reads its type parameter's layout, itself or through another generic member; and
   what that costs in congruent results on `gitextensions-8522`, counted.
3. The decision is implemented. A call that hands a generic member no type of the solution keeps
   the text it has; a test asserts this.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, its tests, and the clarification
criterion 2 produces.

## Tests
In `BodyFingerprinterTests`: `ATypeArgumentsLayoutIsInTheTextOfACallToAMemberThatReadsIt` and
`ACallThatHandsOverNoTypeOfTheSolutionKeepsItsText`.

## Size guard
One pull request.

## Out of scope
- The lowering (P2-150).
- A generic type, as opposed to a generic method, whose members read a type parameter's layout,
  unless criterion 1's repro shows the same gap for it; then it is in.

## Notes
- Filed 2026-10-09 from P2-149.
