# P2-157 Soundness: a struct made with no constructor names a type argument in its interface list, and no text holds it
Status: done (PR #482)
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

### Criterion 1: the repro
- Reproduced on `main` (`aa746102`): 8 cases of
  `ATypeArgumentInAStructsInterfaceListIsInTheTextOfABodyThatMakesOne` failed with equal fingerprints
  before the fix, beside a `Pack = 1` and beside an added field. The Goal is not struck. The cases: a
  `default(K)`; a field of type `K` never assigned; a local of type `K` never assigned; a parameter
  of type `K`; `Make<K>()`, where `Make<T>` returns `default(T)`; a `K[]` and a `List<K>` handed to
  `Enumerable.Cast<W>`; a struct whose interface is nested in a generic type that is handed `S`.
- The ninth case, an array's element (`(new K[1])[0]`), did not compile as first written
  (`new K[1][0]`), so it was first run after the fix and was not seen to fail before it.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024 decision 1 already claims equal
  texts run the same operations, and the clarification of 2026-10-10 (P2-152) named this case as
  left out. Recorded as ADR 0024's clarification of 2026-10-12 (P2-157).
- Deviation: criterion 2 offers a `default` of such a struct, a conversion of one to an interface,
  or none. The rule taken is wider than both: every operation whose type is, or is made of, such a
  struct, and every operation that hands one to a member of the solution as a type argument. A
  `default` misses an array's element, a field no body assigns and a static field, which no body
  makes at all. A conversion misses `Enumerable.Cast<W>(new K[1])`, where a member of a reference
  boxes the elements, and a call through a type parameter. Both are in the repro. The spec and the
  ADR's decision stay as they are.
- The cost, counted on `gitextensions-8522`, compare mode quick, default jobs, one run per column
  on 2026-10-12, exit 5 both (the two constructors of P2-158 fail to lower on both sides of the
  change). Before is `main` at `aa746102`; after is this branch's fix on it. 273 s and 276 s of
  wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 13816 | 13816 |
  | EQ001 | 12792 | 12792 |
  | EQ002 | 29 | 29 |
  | EQ003 | 622 | 622 |
  | EQ004 / EQ005 / EQ006 | 15 / 192 / 166 | 15 / 192 / 166 |
  | `proofMethod` congruence | 12654 | 12654 |
  | `proofMethod` bounded / bounded+refined / lockstep-induction | 124 / 11 / 14 | 124 / 11 / 14 |
  | `matchedPairs` | 13592 | 13592 |
  | `pairsCongruent` | 12654 | 12654 |
  | `changedPairs` | 936 | 936 |

  Compared by location, rule, proof method and Unknown reason: no result changed, none left and
  none is new. The rule costs this pair nothing.
- The scoreboard is not touched: no file under `docs/runs/` is added here.
- Decision: which operations -> every one whose type is, or is made of, the struct, and every one that hands it to a member of the solution. Alternatives: a `default` only; a conversion to an interface only; none. Rule: 3, the repro shows the gap where neither a `default` nor a conversion is in the body.
- Decision: a class -> unchanged; an operation whose type is a class adds nothing. Alternatives: classes too. Rule: 2, the clarification of P2-152 decides it: the body that made the object holds the declaration.
- Decision: where the rule lives -> `Layouts.Implemented`, beside `Layouts.TypeArguments`, called by `BoundSerialiser.Layout`. The lowering does not read it yet (Out of scope; P2-161). Rule: 4.
- The ticket's Spec references name `BoundSerialiser.cs` (`Handed`); that rule moved to `Layouts.TypeArguments` in P2-154, so `Layouts.cs` and `LayoutsTests.cs` are touched too.

### What is left
- P2-161: the lowering. A lowered body that names such a struct is the same beside two
  declarations of `S`, so the pair this text no longer calls congruent can still come back
  Equivalent from the solver. Read off the lowering, not reproduced.
- P2-162: a class created through a type parameter (`Make<K>()`, `new T()`) that names `S` only in
  its interface list. The declaration P2-151 writes for `K` holds its base type and not its
  interfaces. Read off the rule, not reproduced.
- A struct named only as a type argument of a member of a reference, in no operation's type. No
  case was found where the member can run the interface's code; written down in the clarification.
