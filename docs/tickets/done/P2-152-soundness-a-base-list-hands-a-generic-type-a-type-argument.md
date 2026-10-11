# P2-152 Soundness: a type's base list hands a generic type of the solution a type argument, and no text holds it
Status: done (PR #467)
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

### Criterion 1: the repro
- Reproduced on `main` (`8e862692`): all 6 cases of
  `ATypeArgumentInABaseListIsInTheTextOfABodyThatUsesTheType` failed with equal fingerprints before
  the fix, each beside a `Pack = 1` and beside an added field, on one runtime and across one. The
  Goal is not struck. The cases: a body that creates a `D` and calls a member `D` declares; one that
  creates a `D` and calls `Size` through the interface `Box<T>` implements; a type whose base type's
  base type is `Box<S>`; a type whose base type is nested in a generic type handed `S`; a type that
  implements `J<S>`, where `J<T>` has a default implementation; a creation inside a lambda.
- The Goal's first line is narrower than it reads. A call to a member `D` declares is not a gap by
  itself: where that member runs inherited code its own body references a member of `Box<S>`, and
  P2-151 already puts `S` in that body's text. The gap is the creation.
- A constructor that calls its base constructor, written (`: base()`) or added by the compiler,
  has that call in its text, so the rule below reaches it with no extra code
  (`ATypeArgumentInABaseListIsInTheTextOfAConstructorThatCallsTheBaseConstructor`). That test was
  written after the fix and was not run before it.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024 decision 1 already claims equal
  texts run the same operations, and the clarification of 2026-10-09 (P2-151) named this case as
  left out. Recorded as ADR 0024's clarification of 2026-10-10 (P2-152).
- Which text: that of a body that references an instance constructor of the type. It is followed by
  the declaration of every type argument of the type's base types and interfaces that the solution
  declares, and of the types those are nested in. Not a reference to any member: a member the type
  declares has a body with its own pair, and a member it inherits is a member of `Box<S>`, which
  P2-151 covers. Not none: the repro is a false Equivalent by congruence.
- The cost, counted on `gitextensions-8522`, compare mode quick, default jobs, one run per column
  on 2026-10-10, exit 1 both. Before is `main` at `8e862692`; after is this branch's fix on it.
  383 s and 364 s of wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 13818 | 13818 |
  | EQ001 | 12769 | 12769 |
  | EQ002 | 30 | 30 |
  | EQ003 | 568 | 568 |
  | EQ004 / EQ005 / EQ006 | 15 / 192 / 244 | 15 / 192 / 244 |
  | `proofMethod` congruence | 12635 | 12635 |
  | `proofMethod` bounded / bounded+refined / lockstep-induction | 121 / 11 / 13 | 121 / 11 / 13 |
  | `matchedPairs` | 13592 | 13592 |
  | `pairsCongruent` | 12635 | 12635 |
  | `changedPairs` | 957 | 957 |

  Compared by procedure identity, rule, proof method and Unknown reason: no result changed, none
  left and none is new. The rule costs this pair nothing.
- The scoreboard is not touched: no file under `docs/runs/` is added here.
- Decision: which members -> a reference to an instance constructor (an object creation and a constructor initialiser's call). Alternatives: every member of the type; object creations only; none. Rule: 3, the repro shows the gap at the creation and P2-151 covers the other members.
- Decision: which inherited types -> every base type up the chain and every interface, direct or not (`AllInterfaces`), that the solution declares, with the types each is nested in. Alternatives: the base list as written only. Rule: 3, the repro shows the gap for a base type's base type and for an interface.
- Decision: test names -> the two the ticket lists, plus one for a constructor that calls its base constructor. Rule: 4.

### What is left
- A body that is handed the object and creates nothing (`int M(D d) => ((I)d).Size()`) keeps one
  text beside two declarations of `S`. That is the position `int M(Box<S> b) => ((I)b).Size()`
  has under P2-151: the body that made the object holds the declaration (ADR 0019). Not changed.
- P2-157: a struct that names `S` only in its interface list can be made with no constructor
  (`default(K)`, an array's element), so no text holds `S`. Read off the rule, not reproduced.
