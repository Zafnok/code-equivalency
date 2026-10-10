# P2-151 Soundness: a type argument's layout reaches a generic member of the solution that reads it, and neither text holds it
Status: done (PR #451)
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
  unless criterion 1's repro shows the same gap for it; then it is in. (It does, so it is in.)

## Notes
- Filed 2026-10-09 from P2-149.

### Criterion 1: the repro
- Reproduced on `main` (`6a12ae27`): all 14 cases of
  `ATypeArgumentsLayoutIsInTheTextOfACallToAMemberThatReadsIt` failed with equal fingerprints before
  the fix, each beside a `Pack = 1` and beside an added field, on one runtime and across one. The
  Goal is not struck.
- The same gap holds for a generic type: a static method, a property, a static field, an instance
  field, a constructor and a member of a nested type of `Box<S>` all had one fingerprint. By Out
  of scope's second bullet the generic type is in.
- Two more shapes read off the rule and reproduced: a generic local function, and a forwarder with
  no type parameter (`static int SizeOfS() => Size<S>()`), whose caller's text spells the call to
  the target (ADR 0047) and so must hold what that call hands over.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024 decision 1 already claims equal
  texts run the same operations; the clarification of 2026-10-09 (P2-149) applied it to operations
  that read a layout and named this case as left out. Recorded as ADR 0024's clarification of
  2026-10-09 (P2-151).
- Which text: the caller's, for every type argument it hands a member declared in the solution: a
  method's own, and those of the member's type and of the types that one is nested in. Not only
  for a member whose body reads its type parameter's layout: the member a call names is not always
  the one that runs (interface, abstract, virtual), following the parameter through other generic
  members would make a caller's text depend on bodies that have their own pairs (ADR 0019), and
  "reads a layout" would be a second copy of P2-149's list.
- The cost, counted on `gitextensions-8522`, compare mode quick, default jobs, one run per column
  on 2026-10-09, exit 1 both. Before is `main` at `6a12ae27`; after is this branch at `6c16324b`.
  281 s and 267 s of wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 13062 | 13062 |
  | EQ001 | 11984 | 11976 |
  | EQ002 | 32 | 32 |
  | EQ003 | 590 | 598 |
  | EQ004 / EQ005 / EQ006 | 15 / 181 / 260 | 15 / 181 / 260 |
  | `proofMethod` congruence | 11938 | 11844 |
  | `proofMethod` bounded / bounded+refined / lockstep-induction | 41 / 12 / 5 | 120 / 12 / 12 |
  | `matchedPairs` | 13592 | 13592 |
  | `pairsCongruent` | 12682 | 12588 |
  | `changedPairs` | 910 | 1004 |

  Compared by procedure identity: 94 results changed and every one was Equivalent by congruence
  before. 79 are now Equivalent by `bounded`, 7 by `lockstep-induction`, 5 are Unknown (`opaque`)
  and 3 are Unknown (`timeout`). No other result changed, none left and none is new. 33 of the 94
  are in one test class (`GitUITests.GitUICommandsTests.RunCommandTests`).
- Not measured: which of the 94 hand their type to a member that reads a layout. The narrower rule
  would keep at most these 94 congruent.
- The scoreboard is not touched: no file under `docs/runs/` is added here.
- Decision: which members -> every reference to a member whose type the solution declares (call, object creation, method, property, field and event reference). Alternatives: calls only; generic methods only. Rule: 3, the repro shows the gap for each.
- Decision: which callee of a call -> the forwarder's target, as the line spells it (`Called`). Alternatives: the method the source names. Rule: 1.
- Decision: how a handed type is written -> `Marshalled("layout", ...)`, as P2-149 writes one, sharing its once-per-text set. Alternatives: a new line kind. Rule: 4.

### What is left
- P2-152: a type argument named in a base list (`class D : Box<S>`) is in no body's text. Read off
  the rule, not reproduced.
