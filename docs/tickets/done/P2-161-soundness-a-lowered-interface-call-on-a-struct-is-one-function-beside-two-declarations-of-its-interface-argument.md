# P2-161 Soundness: a lowered body that names a struct is the same beside two declarations of a type the struct's interface list hands over
Status: done (PR #486)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-154, P2-157

## Goal
P2-157 writes, after an operation that names a struct, the declaration of every type argument the
struct's interface list hands a generic interface of the solution (ADR 0024, clarification of
2026-10-12 (P2-157)), so a pair that changes only that type is no longer congruent:

    interface W { int Width(); }
    interface J<T> : W where T : unmanaged { int W.Width() => sizeof(T); }
    struct K : J<S> { }
    int M(int x) => ((W)default(K)).Width() + x;      // legacy
    int M(int x) => x + ((W)default(K)).Width();      // modern, beside another declaration of S

Such a pair is then lowered and goes to the solver. The lowered body has a default value of `K`, a
conversion and a call to `W::Width`, none of which names `S`, so they are the same on both sides,
and `J<T>`'s own pair is unchanged. This is P2-154's Goal for the rule P2-157 added:
`Layouts.HandsOver` asks `Layouts.TypeArguments`, and not `Layouts.Implemented`.

Found while working P2-157 by reading the lowering; not reproduced. The first step is the repro.

Struck in part on 2026-10-12 (criterion 1; Notes): the pair above, and `Make<K>().Width()`, were
Unknown before this ticket, since the `default` and the call that is handed `K` are opaques whose
fingerprints hold `S`'s declaration. The Goal holds for a body that is handed the value:

    int M(K k, int x) => ((W)k).Width() + x;          // legacy
    int M(K k, int x) => x + ((W)k).Width();          // modern, beside another declaration of S

## Spec references
ADR 0024 (the clarifications of 2026-10-10 (P2-154) and 2026-10-12 (P2-157)), ADR 0019,
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`Implemented`, `HandsOver`),
`tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A case in `LayoutEquivalenceTests` shows the pair above Equivalent from the solver, or the Goal
   is struck with the test kept. The same for `Make<K>().Width()`, where
   `static W Make<T>() where T : struct, W => default(T)`.
2. Decide through `.claude/skills/equiv-adr`'s bar test what a lowered body does with an operation
   `Layouts.Implemented` gives a type with a layout (`Layouts.IsLaidOut`), and record which row
   applied in `## Notes`, with what it costs on `gitextensions-8522` in compare mode quick, one run
   on each side of the change, counted by rule id.
3. The decision is implemented, the lowering reads the rule the fingerprint reads, and
   `docs/tickets/IOPERATION-COVERAGE.md` says so in the rows it changes. An operation that names a
   struct with no generic interface of the solution is lowered as it was; a test asserts it.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, their tests,
`tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`, `docs/tickets/IOPERATION-COVERAGE.md`,
and the clarification criterion 2 produces.

## Tests
In `LayoutEquivalenceTests`: the cases criterion 1 names. In `IrLowererTests`: one for the decision
and one for criterion 3's second sentence.

## Size guard
One pull request after the decision. If the measured cost in criterion 2 is more than a few pairs,
stop after the measurement and write it up before choosing.

## Out of scope
- The text (P2-157).
- A class handed to a generic member that creates it (P2-162).

## Notes
- Filed 2026-10-12 from P2-157.

### Criterion 1: the repro
- On `main` (`ee1eba4f`) the two pairs the criterion names are not Equivalent: both are Unknown
  with reason `opaque` beside two declarations of `S` (`default(K)` is an opaque with reason
  `DefaultValue`, `Make<K>()` one with reason `Layout` since P2-154 because `K` is itself a struct
  of the solution, and each fragment's fingerprint holds `S`'s declaration since P2-157). So is
  `((W)(new K[1])[0]).Width()`, through the array creation. The Goal is struck for them and the
  tests are kept (`ABodyThatNamesAStructIsNotTheSameBesideTwoDeclarationsOfWhatItsInterfaceListHandsOver`,
  `ABodyThatNamesAStructIsStillSharedBesideOneDeclaration`).
- The Goal's claim does reproduce where no operation of the body makes the struct. Equivalent
  from the solver beside a `Pack = 1` on `S`, before the fix: a parameter of type `K`; a static
  field of type `K` no body assigns; an element of a `K[]` parameter; `new List<K>(1)` handed to
  `Enumerable.Cast<W>`; a `K` parameter handed to `static int Take(W w) => w.Width()`; a
  conditional over two `K` parameters; a local an `out K` argument stores; a `foreach` over a
  `K[]`. Each is a case of `ABodyThatIsHandedAStructIsTheSameBesideOneDeclarationOnlyOfWhatItsInterfaceListHandsOver`.
- Deviation: the Goal's example is not the failing pair; the Goal now says which is. The spec and
  the ADR's decision stay as they are.
- The integration fixture's interface reads `Unsafe.SizeOf<T>()` and not `sizeof(T)`: the test
  compilation does not allow unsafe code.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024's clarification of 2026-10-10
  (P2-154) already decides that a lowered operation the text follows with a declaration is an
  opaque with reason `Layout` where a type it hands over has a layout; this applies it to the rule
  P2-157 added. Recorded as ADR 0024's clarification of 2026-10-12 (P2-161).
- The cost, counted on `gitextensions-8522`, compare mode quick, default jobs (24 processors),
  one run per column on 2026-10-12, exit 1 both. Before is `main` at `ee1eba4f`; after is this
  branch's change on it. 498 s and 446 s of wall-clock time; the before run shared the box with a
  test run, so the times are not compared.

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
  | opaques with reason `Layout`, legacy / modern | 108 / 108 | 108 / 108 |

  Compared by location, rule, proof method and Unknown reason: no result changed, none left and
  none is new. The rule costs this pair nothing, so the Size guard does not trip.
- The scoreboard is not touched: no file under `docs/runs/` is added here.
- Decision: what is opaque -> every operation of such a type that brings the value into the body, the value and not the call that runs the inherited code. Alternatives: the conversion to the interface; the call on the interface; the whole body. Rule: 3, the repro has the code run with no conversion and no interface call in the body (`Take(k)`, `Enumerable.Cast<W>`).
- Decision: what is left out -> a constant, an assignment, an increment or decrement, and a read of a flow capture that holds a value. Alternatives: no exception (an opaque in place of an assignment loses its store; a flow capture has no fingerprint, so every conditional over such a value would be Unknown); reads of locals left out too (an `out` argument, a `foreach` and a pattern store a local with no operation that yields the value). Rule: 1, soundness first, then precision.
- Decision: where the rule lives -> `Layouts.Inherits`, which asks `Layouts.Implemented` of the operation's type and `Layouts.IsLaidOut` of what it gives. The type arguments an operation hands over need no second question: a struct handed over is itself a type with a layout, so `Layouts.HandsOver` already holds. Rule: 4.
- Decision: the `default` the compiler passes for an optional struct parameter -> still the constant of P2-138. Rule: 2, ADR 0019: only a member with a parameter of that type takes it, and its own pair holds the declaration.

### Criterion 3
- `IrLowererTests`: `AnOperationThatBringsInAStructWhoseInterfaceListHandsOverATypeIsAnOpaqueWhoseFingerprintHoldsTheDeclaration`
  and `AnOperationThatStoresOrHoldsSuchAStructIsLoweredAsItWas` for the decision,
  `AnOperationThatNamesAStructWithNoGenericInterfaceIsLoweredAsItWas` for the second sentence.
  `LayoutsTests` has the rows for `IrLowerer.DependsOnLayout`.
- `IOPERATION-COVERAGE.md`: the rule applies to every row, so it is one paragraph above the table,
  and the rows `ParameterReference`, `LocalReference`, `FieldReference` and
  `ArrayElementReference` say it themselves.

### What is surprising
- `F ??= default(K)` on a field of type `K?` was Equivalent beside one declaration and is now
  Unknown: the read the graph makes of the captured place is an opaque with no fingerprint. No
  ticket is filed for it: the measured cost is nothing.
- A step through a user-defined `++` of such a struct reads its target without an opaque. The
  operator is a member of the solution with a parameter of the struct's type, so its own pair
  holds the declaration (ADR 0019), and the next read of the local is an opaque.
