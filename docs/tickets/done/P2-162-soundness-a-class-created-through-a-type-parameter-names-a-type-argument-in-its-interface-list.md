# P2-162 Soundness: a class created through a type parameter names a type argument in its interface list, and no text holds it
Status: done (PR #485)
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

### Criterion 1: the repro
- Reproduced on `main` (`ee1eba4f`): all 5 cases of
  `ATypeArgumentInAClasssInterfaceListIsInTheTextOfABodyThatHandsTheClassOver` failed with equal
  fingerprints before the fix, beside a `Pack = 1` and beside an added field. The Goal is not
  struck. The cases: `Make<K>()`, where `Make<T>` runs `new T()`; `Maker<K>.Make()`, a member of a
  generic type; `Make<Derived>()`, where `Derived : K`; `Count<K>()`, where `Count<T>` calls a
  static member of the interface on its type parameter (`T.Count()`), which runs `J<S>`'s code
  with no object at all; `Made<Maker<K>>()`, a type argument of a type that is handed over.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024 decision 1 already claims equal
  texts run the same operations, and the clarification of 2026-10-12 (P2-157) named this case as
  left out. Recorded as ADR 0024's clarification of 2026-10-12 (P2-162).
- The rule taken is the first the criterion offers: a reference that hands a member of the
  solution a type also hands over what the type's interface list does. It is P2-157's rule for a
  struct that is handed over, without the test for a struct.
- The cost, counted on `gitextensions-8522`, compare mode quick, default jobs, one run per column
  on 2026-10-12, exit 1 both. Before is `main`'s `src/` at `ee1eba4f`; after is this branch's.
  337 s and 312 s of wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 13818 | 13818 |
  | EQ001 | 12792 | 12792 |
  | EQ002 | 29 | 29 |
  | EQ003 | 624 | 624 |
  | EQ004 / EQ005 / EQ006 | 15 / 192 / 166 | 15 / 192 / 166 |
  | `proofMethod` congruence | 12654 | 12654 |
  | `proofMethod` bounded / bounded+refined / lockstep-induction | 124 / 11 / 14 | 124 / 11 / 14 |
  | `matchedPairs` | 13592 | 13592 |
  | `pairsCongruent` | 12654 | 12654 |
  | `changedPairs` | 938 | 938 |

  Compared by procedure identity, rule, proof method and Unknown reason: no result changed, none
  left and none is new. The rule costs this pair nothing.
- The scoreboard's numbers are not touched: no file under `docs/runs/` is added here. The README's
  two links to this ticket now name P2-168, the soundness ticket this one leaves open, so the
  count of open soundness tickets it states is still two.
- Decision: which types -> every type handed to a member of the solution, and the types it is made of. Alternatives: only where the member has a `new()` constraint; only where its body has a `new T()`; classes only. Rule: 3, the repro's `Count<K>()` runs the inherited code with no `new()`, and P2-151's clarification already rejects reading the member's body.
- Decision: an operation whose type is the class -> unchanged, adds nothing. Alternatives: P2-157's rule for a struct. Rule: 2, the clarification of P2-152 decides it: the body that made the object holds the declaration.
- Decision: where the rule lives -> `Layouts.Implemented`, which takes the types handed over as a second argument. Alternatives: a second method; inside `TypeArguments`, which the lowering reads (`HandsOver`). Rule: 4, one place for what an interface list hands over, and the lowering is P2-161's.
- The ticket's criterion 3 case, `int M(K k) => ((W)k).Width()`, was already asserted for a class
  in P2-157's `ADefaultOfAStructWithNoGenericInterfaceKeepsItsText`; the new test has it beside
  four more.

### What is left
- P2-161: the lowering, as before. A note there says `Make<K>()` on a class is now one of its
  cases.
- P2-168: a type of the solution created by a generic member of a reference
  (`Activator.CreateInstance<K>()`, `new Lazy<K>().Value`). No constructor is referenced and a
  member of a reference is handed nothing, so neither the base type nor the interface list
  reaches `S`. Read off the rule, not reproduced.
