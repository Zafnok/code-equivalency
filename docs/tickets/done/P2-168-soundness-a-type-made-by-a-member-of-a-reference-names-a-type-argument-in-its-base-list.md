# P2-168 Soundness: a type a member of a reference makes names a type argument in its base list, and no text holds it
Status: done (PR #494)
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

### Criterion 1: the repro
- Reproduced on `main` (`e98fd1b9`): 6 of the 7 cases first written for
  `ATypeArgumentInABaseListIsInTheTextOfABodyThatHandsTheTypeToAReference` failed with equal
  fingerprints, beside a `Pack = 1` and beside an added field. The Goal is not struck. The cases:
  `((W)Activator.CreateInstance<K>()).Width()` and `((W)new Lazy<K>().Value).Width()` (an
  interface), `((I)Activator.CreateInstance<D>()).Size()` (a base type),
  `((I)new Lazy<E>().Value).Size()` (a base type's base type), `Activator.CreateInstance<F>()` (a
  type the base type is nested in), `new Lazy<Tuple<K>>()` (a type argument of a type handed
  over). The seventh, the Goal's `N`, `Activator.CreateInstance<D>().Instance`, already had a
  fingerprint per declaration: `Instance` is a member of `Box<S>`, and a reference to it hands `S`
  over (P2-151). Two cases were added with the fix: `Enumerable.Repeat(k, 1)`, which creates no
  `K`, and `Activator.CreateInstance<Box<S>>()`, a generic type of the solution created itself;
  the second failed without the widening the second Decision below names.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024 decision 1 already claims equal
  texts run the same operations, and the clarification of 2026-10-12 (P2-162) named this case as
  left out. Recorded as ADR 0024's clarification of 2026-10-14 (P2-168).
- The rule taken is the first the criterion offers: every member of a reference that is handed a
  type of the solution, and not only those known to create it.
- The cost, counted on `gitextensions-8522`, compare mode quick, default jobs, one run per column
  on 2026-10-14, exit 1 both. Before is `main`'s `src/` at `e98fd1b9`; after is this branch's.
  308 s and 356 s of wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 13818 | 13818 |
  | EQ001 | 12794 | 12794 |
  | EQ002 | 29 | 29 |
  | EQ003 | 622 | 622 |
  | EQ004 / EQ005 / EQ006 | 15 / 192 / 166 | 15 / 192 / 166 |
  | `proofMethod` congruence | 12654 | 12654 |
  | `proofMethod` bounded / bounded+refined / lockstep-induction | 126 / 11 / 14 | 126 / 11 / 14 |
  | `matchedPairs` | 13592 | 13592 |
  | `pairsCongruent` | 12654 | 12654 |
  | `changedPairs` | 938 | 938 |
  | opaques with reason `Layout`, each side | 108 | 108 |

  Compared by procedure identity, rule, proof method and Unknown reason: no result changed, none
  left and none is new. The rule costs this pair nothing.
- A first after-run, before the tuple fix in `Layouts.Arguments`, cost 15 congruent results: 15
  test bodies of `GitUITests.UserControls.RevisionGrid.Graph.LaneInfoProviderTests` hand
  NSubstitute's `Returns` a tuple `(RevisionGraphRevision, bool)`, and Roslyn gives a tuple type
  syntax as its declaration (the quirk `Layouts.IsLaidOut` already names), so the tuple counted as
  a generic type of the solution and its element's declaration was written
  (`properties.notCongruent`: component `type-layout`, identity `RevisionGraphRevision`, which the
  pull request edits). All 15 stayed Equivalent, 13 by `bounded` and 2 by `lockstep-induction`. A
  tuple runs no code of the solution when it is created, so the fix leaves it out;
  `ATypeWithNoGenericBaseHandedToAReferenceKeepsItsText` has the case.
- The scoreboard's numbers are not touched: no file under `docs/runs/` is added here. The README's
  two links to this ticket go, and the soundness row says no soundness ticket is open, since none
  other is.
- Decision: which members of a reference -> every one. Alternatives: a list of the members known to create their type argument (`Activator.CreateInstance<T>`, `Lazy<T>`); none, with the limit written down. Rule: 3, a list is never complete and a member left off is a false Equivalent, and P2-151's clarification already refuses to read a member's body; the cost is counted above.
- Decision: what a type handed over hands on -> the type arguments of its base types and interfaces, and its own type arguments where it is a generic type of the solution; not its own declaration. Alternatives: the base list only, as the ticket's Goal says; the type's whole declaration, as P2-151 writes for a member of the solution. Rule: 1, `Activator.CreateInstance<Box<S>>()` is the same hole (P2-151 and P2-152 wrote it down as a limit), and the declaration of every `List<K>`'s `K` would cost congruence for what P2-149 already decides.
- Decision: where the rule lives -> a new `Layouts.Inherited`, which the text (`BoundSerialiser.Layout`) and the lowering (`Layouts.HandsOver`) both read. Alternatives: inside `TypeArguments`, which writes the handed types' own declarations; a second rule in the lowering. Rule: 4, one place, as P2-154 and P2-161 keep the two halves.
- Deviation: the ticket puts the lowering out of scope as P2-161's, and P2-161 is done. Its clarification says the lowering holds what the text holds, so `Layouts.HandsOver` asks `Layouts.Inherited` too; without it `Activator.CreateInstance<N.K>` would be one function on both sides of a pair the text no longer calls congruent. `IrLowererTests.AReferenceThatHandsAMemberOfAReferenceAClassIsAnOpaqueWhereItsBaseListHandsOverATypeWithALayout` asserts it.
- Deviation: P2-162's `ABodyThatIsHandedAnObjectOfSuchAClassKeepsItsText` had a case,
  `Enumerable.Repeat(k, 1)`, that hands `K` to a generic member of a reference; under this rule it
  holds `S`. It moved to this ticket's test, and the P2-162 case now hands `Repeat<object>` the
  object, which is what that test's summary describes.

### Criterion 3
- `ATypeWithNoGenericBaseHandedToAReferenceKeepsItsText`: a class with no base list, one whose
  base type is a reference's generic type, one whose base type is handed a type from a reference,
  one handed a type parameter, a struct with no generic interface of the solution, and a body that
  is handed an object of `D` and hands its type to no member.
