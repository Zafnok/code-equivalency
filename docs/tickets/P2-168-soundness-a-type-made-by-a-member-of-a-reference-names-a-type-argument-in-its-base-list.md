# P2-168 Soundness: a type a member of a reference makes names a type argument in its base list, and no text holds it
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-162

## Goal
P2-152 holds what a class's base list hands over in the text of a body that references the class's
constructor, and P2-162 in the text of a body that hands the class to a member of the solution
(ADR 0024, clarifications of 2026-10-10 (P2-152) and 2026-10-12 (P2-162)). A class can be created
by neither, by a generic member of a reference that is handed it:

    interface W { int Width(); }
    interface J<T> : W where T : unmanaged { int W.Width() => sizeof(T); }
    class K : J<S> { }
    class D : Box<S> { }                                   // Box<T> has a field `= sizeof(T)`
    int M() => ((W)System.Activator.CreateInstance<K>()).Width();
    int N() => System.Activator.CreateInstance<D>().Instance;
    int O() => new System.Lazy<K>().Value.Width();

`M`'s text names `K` and no constructor of it, and `Activator` is a type of a reference, which
`Layouts.TypeArguments` says hands the solution nothing. So nothing reaches `S`, for the interface
list and for the base type alike. A pair that changes only `S`'s fields has equal fingerprints for
`M`, `N` and `O`.

Found while working P2-162 by reading its rule; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarifications of 2026-10-09 (P2-151), 2026-10-10 (P2-152) and 2026-10-12 (P2-157
and P2-162)), ADR 0019, `src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`TypeArguments`,
`Implemented`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test on `BodyFingerprinter` shows `M`, `N` and `O` above each with one fingerprint beside
   two declarations of `S`, or the Goal is struck with the test kept.
2. Decide through `.claude/skills/equiv-adr`'s bar test which text holds the declaration (a
   reference that hands a member of a reference a type of the solution also hands over what the
   type's base list does, for every such member or for the ones known to create their type
   argument, or none and the limit stays written down), and record which row applied in
   `## Notes`, with what it costs in congruent results on `gitextensions-8522`, counted.
3. The decision is implemented, and a test asserts that a body that hands a member of a reference
   a type with no generic base type or interface of the solution keeps the text it has.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs`, its tests, and the clarification criterion 2
produces.

## Tests
In `BodyFingerprinterTests`: `ATypeArgumentInABaseListIsInTheTextOfABodyThatHandsTheTypeToAReference`
and `ATypeWithNoGenericBaseHandedToAReferenceKeepsItsText`.

## Size guard
One pull request.

## Out of scope
- The lowering (P2-161).
- Creation by reflection from a `System.Type` or a name (`Activator.CreateInstance(typeof(K))`),
  which ADR 0024 already writes down as a limit.

## Notes
- Filed 2026-10-12 from P2-162.
