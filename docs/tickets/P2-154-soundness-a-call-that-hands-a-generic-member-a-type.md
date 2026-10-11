# P2-154 Soundness: a lowered call that hands a generic member of the solution a type is one function beside two declarations of the type
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-150, P2-151

## Goal
P2-151 writes, after a reference to a generic member of the solution, the declaration of every type
it hands the member (ADR 0024, clarification of 2026-10-09 (P2-151)), so a pair that changes only
the type is no longer congruent:

    static int Size<T>() where T : unmanaged => sizeof(T);
    int M(int x) => Size<S>() + x;      // legacy
    int M(int x) => x + Size<S>();      // modern, beside another declaration of S

Such a pair is then lowered and goes to the solver. The call is an `IrCall` whose identity names
`Size<N.S>` and nothing of `S`'s declaration, so it is one function for both sides, and `Size`'s
own pair is unchanged (ADR 0019 does not catch it: a type has no pair). This is the second bullet of
P2-150's Goal for a callee P2-150's rule does not reach: `Size` is not under
`System.Runtime.InteropServices` and takes no pointer, so `Layouts.IsHanded` is false for it.

P2-150 and P2-151 were written side by side and each left this to the other. Found while merging
them; not reproduced. The first step is the repro.

## Spec references
ADR 0024 (the clarifications of 2026-10-09 (P2-150) and (P2-151)), ADR 0019, ADR 0014,
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`IsHanded`),
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Handed`),
`tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A case in `LayoutEquivalenceTests` shows the pair above Equivalent from the solver, or the Goal
   is struck with the test kept. The same for a member of a generic type of the solution
   (`Box<S>.Size()`).
2. Before any change to the lowering: decide through `.claude/skills/equiv-adr`'s bar test what a
   lowered body does with such a call, and record which row applied in `## Notes`. P2-151 measured
   what its rule costs congruence on `gitextensions-8522`; the decision says what the same rule
   would cost the lowering if every such call became an opaque, from one run of that pair in
   compare mode quick on each side of the change, counted by rule id.
3. The decision is implemented, the rule lives in one place that the fingerprint and the lowering
   both read, and `docs/tickets/IOPERATION-COVERAGE.md` says so in the rows it changes. A call that
   hands a generic member of the solution only types from a reference or type parameters is the
   call it was; a test asserts it.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, `src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`,
their tests, `tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`,
`docs/tickets/IOPERATION-COVERAGE.md`, and the ADR or clarification criterion 2 produces.

## Tests
In `LayoutEquivalenceTests`: the cases criterion 1 names. In `IrLowererTests`: one for the decision
and `ACallThatHandsAGenericMemberNoDeclaredTypeIsStillACall`.

## Size guard
One pull request after the decision. If the measured cost in criterion 2 is more than a few pairs,
stop after the measurement and write it up before choosing.

## Out of scope
- A type argument named in a base list (P2-152).
- An auto-property or an event of an explicit layout (P2-153).
- A type from a reference.

## Notes
- Filed 2026-10-09 from P2-150, at the merge with P2-151.

### Criterion 1: the repro
- Reproduced on `main` at `8e862692`, and the Goal is not struck:
  `ACallThatHandsAGenericMemberATypeIsNotOneFunctionBesideTwoDeclarations` failed all four of its
  cases with `Actual: Equivalent` before the change: `Size<S>()`, `Box<S>.Size()`, a static
  property `Box<S>.Width` and `new Box<S>().Count`, each beside a `Pack = 1` on `S`, in bodies that
  differ by the order of a sum. The generic members read `Unsafe.SizeOf<T>()`, so the snippet
  needs no `unsafe`.
- `ACallThatHandsAGenericMemberATypeIsStillSharedBesideOneDeclaration` held before and holds after.

### Criterion 2: the bar test and the measurement
- `equiv-adr` bar test, row 1: ADR 0024 decision 2 already says what an operation the lowering
  does not model is, and its clarifications of 2026-10-09 (P2-150) and (P2-151) each left this
  case to this ticket. The vehicle is the clarification of 2026-10-10 (P2-154) under ADR 0024, in
  this pull request. No Core contract, verdict, rule id or SARIF shape changes.
- `gitextensions-8522`, compare mode quick, default jobs, one run per column on 2026-10-10, exit 1
  each. Before is `main` at `8e862692`. "Every declared type" is the rule the ticket asks the cost
  of: an opaque wherever P2-151 writes a declaration. "Laid out" is the rule this pull request has.
  336 s, 383 s and 246 s of wall-clock time.

  | | before | every declared type | laid out |
  |---|---|---|---|
  | results | 13818 | 13818 | 13818 |
  | EQ001 | 12769 | 12682 | 12769 |
  | EQ002 | 30 | 31 | 30 |
  | EQ003 | 568 | 657 | 569 |
  | EQ004 / EQ005 | 15 / 192 | 15 / 192 | 15 / 192 |
  | EQ006 | 244 | 241 | 243 |
  | `proofMethod` congruence | 12635 | 12635 | 12635 |
  | `proofMethod` bounded / bounded+refined / lockstep-induction | 121 / 11 / 13 | 41 / 11 / 6 | 121 / 11 / 13 |
  | `pairsCongruent` / `changedPairs` | 12635 / 957 | 12635 / 957 | 12635 / 957 |
  | `changedPairsWithoutOpaque` | 485 | 400 | 484 |
  | `opaqueByReason.Layout`, legacy / modern | 25 / 25 | 481 / 480 | 108 / 108 |

- Every declared type, compared with before by procedure identity: 94 results change. 87 were
  Equivalent and are Unknown (84 `opaque`, 3 `timeout`), 4 go from EQ006 to Unknown
  (`abstraction`), and one each from Unknown to EQ002, from Unknown to EQ006 and from Unknown
  (`abstraction`) to Unknown (`opaque`). 33 are in `GitUITests.GitUICommandsTests.RunCommandTests`.
  They are the pairs P2-151 took out of congruence: the solver had proved 86 of them with the
  call as one function. In all of them the type handed over is a class: a form handed to a generic
  launcher, an argument of `Validates.NotNull<T>`, a tree node, a cache's key or value.
- That trips the Size guard, which asks for this write-up before the choice. The choice was then
  made here and not handed back: a narrower rule that is sound for these pairs was at hand and
  measured.
- Laid out, compared with before: one result changes,
  `GitUI.Editor.FileViewer::CopyToolStripMenuItemClick(object,System.EventArgs)`, from EQ006 to
  Unknown (`abstraction`): its divergence now depends on a `Layout` opaque. No Equivalent is lost.
- Decision: what a lowered body does with such a call -> an opaque with reason `Layout`, shared by its fingerprint, as P2-150 decided for the callees it reaches. Alternatives: the declaration in the call's identity (P2-150 rejected it: two identities are a trace that differs, a Divergent where nothing is known). Rule: 1, then 4.
- Decision: for which types handed over -> one that is, or is made of, a value type of the solution or a class of the solution that has a `[StructLayout]` (`Layouts.IsLaidOut`). Alternatives: every type the text writes a declaration for (87 Equivalents on the measured pair, for classes whose size through a type parameter is a reference's whatever they declare); only when the member's body reads a layout (P2-151 rejected reading bodies: the member a call names is not always the one that runs). Rule: 1 holds for both, then 2.
- Decision: which operations -> every one P2-151 follows with a declaration: a call, an object creation, and a read or a write of a property, a field or an event. Alternatives: calls only (a static field of `Box<S>` is initialized by the generic type's code, which no body calls by name, so nothing else would answer for it). Rule: 1. A method group converted to a delegate needs nothing: it is the pure function of a fingerprint that already holds the declaration.
- Decision: where the rule lives -> `Layouts.TypeArguments` (moved from `BoundSerialiser.Handed`; the text is unchanged) and `Layouts.HandsOver`, read by `BoundSerialiser.Layout`, `IrLowerer.ReadsLayout` and `IrLowerer.DependsOnLayout`. Alternatives: a copy in the lowering. Rule: 4.
- Decision: the IL lowering -> a method with such an operation is not lowered from IL, by the member it names or by a forwarder's target, kept or not. Alternatives: nothing (the IL pass would read the pairs this makes Unknown with the call as one function again). Rule: 1.
- Not run: compare mode thorough. The scoreboard is not touched: no file under `docs/runs/` is added.

### Criterion 3: what changed
- `ACallThatHandsAGenericMemberNoDeclaredTypeIsStillACall` asserts the callee identities for type
  arguments from a reference, type parameters, a non-generic member, and a class, an interface
  array and a tuple of a class of the solution.
- Found on the way: Roslyn gives a tuple type its syntax as a declaring reference, so
  `Layouts.IsDeclared` is true of every tuple, `(int, int)` included. The text is left as it is;
  `IsLaidOut` asks a tuple's elements. An anonymous type has one too, and a call that hands one to
  a generic member stays a call (`AnonymousObjectLoweringTests` held the first attempt to that).
