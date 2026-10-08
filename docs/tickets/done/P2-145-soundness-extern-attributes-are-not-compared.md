# P2-145 Soundness: the attributes of an `extern` function are its code, and nothing compares them
Status: done (PR #434)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-107

## Goal
An `extern` function has no bound code. Its attributes name the library and the entry point it
calls and say how its arguments are marshalled (ADR 0024, clarification of 2026-10-07). `equiv`
reads those attributes in one place only, a partial method's implementing part on a same-runtime
pair (P2-107). Everywhere else a changed `[DllImport]` is invisible. Two gaps follow. Both were
confirmed on `main` (`c59fa0fc`) on 2026-10-08 with a throwaway unit test, since deleted, while
working P2-107, and both are older than that ticket.

### Gap 1: a false Equivalent by congruence
`BoundSerialiser` serialises the bound `IOperation` tree of a body. An attribute is not an
operation, so the attributes of a local function the body declares are not in the text. Two bodies
that differ only in the `[DllImport]` of a local `extern` function have the same fingerprint, on
any runtime pair. The pair is congruent (ADR 0024 decision 1) and is reported `EQ001` with
`proofMethod: congruence`. The solver is never asked, so P2-127's `LocalFunction` opaque does not
come into it.

Repro, as members of a class:

```csharp
public int M()
{
    return F();

    [System.Runtime.InteropServices.DllImport("a.dll")]   // other side: "b.dll"
    static extern int F();
}
```

`BodyFingerprinter.Compute` returns equal fingerprints for the two sides.

### Gap 2: no result at all
`ProcedureEnumerator.IsIncluded` leaves out every method whose `IsExtern` is true, and Roslyn
reports a partial definition whose implementing part is `extern` as `extern`. So a `[DllImport]`
method whose library, entry point or marshalling differs between the two sides produces no
Equivalent, no Divergent and no Unknown: it is not a matched pair. Its callers stay congruent,
because the callee's identity did not change, and each caller's verdict lists it as an assumed
callee (ADR 0019) that no pair in the run checks.

Repro, as members of a class:

```csharp
[System.Runtime.InteropServices.DllImport("a.dll")]   // other side: "b.dll"
public static extern int F();

public int M() { return F(); }
```

`ProcedureEnumerator.Enumerate` returns `M` and not `F` on both sides, and `M` is congruent.

## Spec references
ADR 0024 (`docs/adr/0024-congruence-by-bound-fingerprint.md`: decision 1, and the 2026-10-07
clarification's "What makes the two bodies the same function", which says an `extern` function's
attributes are its code). ADR 0019 (`docs/adr/0019-modular-verdicts-name-assumptions.md`: a
verdict assumes its callees, and a callee's change is caught by the callee's own pair).
`docs/tickets/done/P2-107-identical-opaque-bodies-on-one-runtime.md`, Notes, "Found, not fixed
here". `src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Serialise`,
`SerialiseImplementingPart`, `Attributes`),
`src/Equiv.Frontend.CSharp/Fingerprinting/BodyFingerprinter.cs`,
`src/Equiv.Frontend.CSharp/ProcedureEnumerator.cs` (`IsIncluded`), ARCHITECTURE.md's frontend step
2 ("all with a body") and M2-002's acceptance criterion 1, which excluded `extern` members.

## Acceptance criteria (all must hold; nothing beyond them)
1. Gap 1's repro is a unit test that fails before the fix: the two bodies have different
   fingerprints. The same test holds the control: two sides with the same `[DllImport]` have equal
   fingerprints, also when one side names the library through a constant.
2. `BoundSerialiser` writes the attribute lines of every local function a body declares (on the
   function, its return value and its parameters, as `Attributes` writes them), for every body and
   on every runtime pair, not only in `SerialiseImplementingPart`. A body that declares no local
   function with an attribute keeps the text it has today; a test asserts this.
3. Before any change to `ProcedureEnumerator`: decide how an `extern` method is compared, through
   `.claude/skills/equiv-adr`'s bar test, and record which row applied in `## Notes`. The decision
   says, at least:
   - whether an `extern` method is enumerated and matched (so that an added or removed one is
     `EQ004` or `EQ005`, as any member is);
   - what a matched pair gets. The candidate: congruent only when the signature and the bound
     attributes of the method, its return value and its parameters are equal, otherwise Unknown;
   - what a pair that crosses a runtime gets. P2-107's clarification holds that the runtime
     marshals an interop call and that no `runtime-changes.json` row describes it;
   - which `extern` members it covers: a `[DllImport]` method, a partial method whose implementing
     part is `extern`, an `extern` method with no `[DllImport]` (`MethodImplOptions.InternalCall`),
     and `extern` constructors, accessors and operators;
   - the Unknown's reason, taken from the reasons that exist where one fits.

   A new ADR goes in its own pull request first, as that skill says. Do not hand the design choice
   back to the owner: decide, write the soundness argument, and let the review of that pull
   request be the check.
4. The decision of criterion 3 is implemented. Gap 2's repro is a unit test and an integration
   test, and both fail before the fix: `compare` on the pair reports a result for `F`, and that
   result is not `EQ001`. An unedited `extern` method gets the verdict the decision gives it.
5. ARCHITECTURE.md's frontend step 2, `ProcedureEnumerator`'s doc comment and
   `ProcedureEnumeratorTests.ExcludesLocalFunctionsLambdasImplicitAbstractExtern` say what criterion
   3 decided.
6. The public-corpus results that change are counted in `## Notes`, by rule id before and after,
   from one run of `powershell-19687` in compare mode quick on each side of the change (through
   `equiv-corpus-run`). That pair has the interop code: all 46 of its `no-body` pairs were
   `[LibraryImport]` methods.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BodyFingerprinter.cs`,
`src/Equiv.Frontend.CSharp/ProcedureEnumerator.cs`, their tests, an integration test for criterion
4, `docs/ARCHITECTURE.md`, and the ADR or clarification criterion 3 produces.

## Tests
In `BodyFingerprinterTests`:
`BodiesThatDifferOnlyInALocalExternFunctionsAttributesAreNotCongruent` (criterion 1, with the
library, the entry point, a `[return: MarshalAs]` and a parameter's `[MarshalAs]` as cases, on a
same-runtime pair and on one that crosses a runtime),
`ABodyWithoutAttributedLocalFunctionsKeepsItsText` (criterion 2), and
`AnExternMethodIsFingerprintedByItsSignatureAndAttributes` or the name the decision calls for
(criterion 4). In `ProcedureEnumeratorTests`: the rewritten exclusion test and one for a partial
method whose implementing part is `extern`. The integration test for criterion 4, on a sample
under `samples/`.

## Size guard
Two pull requests at most after the decision: gap 1, then gap 2. Gap 1 does not wait for the
decision. If the decision makes an `extern` pair anything other than congruent or Unknown, stop
and file the rest.

## Out of scope
- Lowering an `extern` call, or modelling what a native function does. The native function is a
  callee outside both solutions, as a base class library member is.
- The attributes of an ordinary method itself, of a lambda, and of a type: none is in the
  fingerprint of a body today, and whether one of them can change what a body does was not checked
  here. File what is found; do not fix it in this ticket.
- Lowering a partial method's implementing part (the limit P2-107 recorded).
- Code a source generator emits on a pair that crosses a runtime (P2-118).

## Notes
- Found 2026-10-07 and 2026-10-08 while working P2-107 (pull requests #422 and #425). P2-107 added
  `BoundSerialiser.Attributes` and calls it from `SerialiseImplementingPart` alone, which is why
  this ticket depends on it.
- Not counted: how many results on the public corpus rest on either gap. Criterion 6 counts what
  changes; a result changes only where a `[DllImport]` exists, and is wrong today only where one
  was edited.
- Until this lands, an Equivalent verdict does not cover the attributes of an `extern` function
  the member declares or calls.

### Gap 1 (criteria 1 and 2)
- Bar test for gap 1 (`equiv-adr`): the first row. ADR 0024's clarification of 2026-10-07 already
  says a local `extern` function's attributes are its code; this applies it to every text. Recorded
  as ADR 0024's clarification of 2026-10-08.
- Decision: the attribute lines are written where the local function's own line is written
  (`BoundSerialiser.Visit`), not after the body as `SerialiseImplementingPart` did. One place then
  covers a body, an implementing part and a fragment, and a local function a lambda declares.
  Rejected: a loop over the body's descendants in `Serialise`, which would leave a fragment's text
  without them. The text of an implementing part that declares an attributed local function
  changes (the same lines, earlier); nothing stores a fingerprint between runs.
- Found while writing it, and fixed by the same line: a lambda that declares a local `extern`
  function was one `delegate:<fingerprint>` function on both sides whatever its `[DllImport]`
  said, so the solver would have proved the pair Equivalent after the body fingerprints differed.
  `FragmentFingerprinterTests.ALambdasLocalExternFunctionsAttributesAreInItsFingerprint` fails
  without the fix.
- Decision: an `extern` function also has an `Extern` line holding its import as Roslyn resolves it
  (`IMethodSymbol.GetDllImportData`). Measured with a throwaway program that compiled and read the
  metadata: a local function `F` with `[DllImport("a.dll")]` and no `EntryPoint` is emitted as the
  import `a.dll!F`, although the method is named `<M>g__F|0_0`. The text numbers local functions,
  so without that line renaming such a function would change what the body calls and keep its
  fingerprint. The line also holds the character set, which the module's `[DefaultCharSet]`
  supplies when the attribute names none. A body with no `extern` local function has no such line.
- Not fixed, filed as P2-146: the settings the marshaller reads from outside the function (the
  assembly's `[DisableRuntimeMarshalling]` and `[DefaultDllImportSearchPaths]`, a containing
  type's `[BestFitMapping]`, the layout of the types in the signature). Read off what the text
  holds, not reproduced.
- A limit: an `extern` local function with no `[DllImport]` is written by its containing type and
  its source name. The name a runtime would look such a function up by is the compiler's
  (`<M>g__F|0_0`), which also depends on the containing member. No runtime that `equiv` knows
  implements one.
- Criterion 2's "keeps the text it has today":
  `BodyFingerprinterTests.ABodyWithoutAttributedLocalFunctionsKeepsItsText` is a snapshot taken
  on `main` (`40057429`) before the change and unchanged by it.

### Criterion 3: the decision
- Bar test (`equiv-adr`): the fourth row, a new ADR. The decision reverses a spec row
  (ARCHITECTURE.md's frontend step 2 as M2-002 criterion 1 built it, which left `extern` members
  out) and extends what an Equivalent covers to a member with no bound body. The first row did
  not fit: ADR 0024 says an `extern` function's attributes are its code, but no accepted ADR says
  which members are procedures. ADR 0054, pull request #432, before any change to
  `ProcedureEnumerator`.
- The five points, as ADR 0054 decides them:
  - Enumerated and matched: yes, so an added or removed `extern` member is `EQ004` or `EQ005`.
  - A matched pair on one runtime is Equivalent by congruence when the fingerprints are equal:
    the signature line, the import as the compiler resolves it, and the bound attributes of the
    method, its return value and its parameters. Otherwise Unknown. Never Divergent.
  - A pair that crosses a runtime has no fingerprint and is Unknown. A body that declares an
    `extern` local function is runtime-sensitive there, so the rule is not escaped by moving the
    import inside a method.
  - Members: a `[DllImport]` method; a partial method whose implementing part is `extern`; an
    `InternalCall` method, whose text holds its declared type and name in place of an import;
    `extern` constructors, accessors and operators by the same rule. An `extern` member with
    neither `[DllImport]` nor `InternalCall` has no fingerprint and is Unknown on every pair.
  - The Unknown's reason is the existing one: both sides lower, as before, to a whole-body
    opaque with reason `no-body`, and the result is `unknownReason: opaque` naming it.
- Decided beyond the five: `--execute` never calls an `extern` method (decision 6). A generated
  driver would pass made-up handles and pointers to native code.
- Size guard: an `extern` pair is congruent or Unknown and nothing else, so the stack went on.

### Gap 2 (criteria 4 to 6)
- Before the fix, `main`'s build (`40057429`) on `samples/extern-import`: one result, `M`,
  `EQ001` by congruence, no `assumedCallees`. After: `F` is `EQ003` (`opaque`, `no-body`), the
  unedited `Ticks` is `EQ001` by congruence, and `M` is `EQ001` by congruence with `F` in
  `assumedCallees` and `unprovenAssumptions`.
- Decision: the integration test is a new sample, `samples/extern-import`, and not three more
  members of `same-runtime-cleanup`. One concept per sample, and P2-107's assertions on that
  sample (three results, exit code 0) stay as they are.
- Decision: the obstacle for `--execute` is in `ReplayArguments.CallObstacle`, which every driver
  for a solution's own member goes through. `DriverFactory.Obstacles` is `runtime-diff`'s, for
  base class library members, and is left alone.
- Criterion 5: the exclusion test is now
  `ProcedureEnumeratorTests.ExcludesLocalFunctionsLambdasImplicitAbstract`; the ticket names it
  with its old `...Extern` ending. VERIFICATION-MODEL.md section 1 said a declaration with no
  bound code has no fingerprint, with the partial method as the one exception; it now names the
  `extern` method as the second.
- `IlLowererTests` read the ILAst of every sample procedure and took it to exist. An `extern`
  method has none (`il-no-body`, which production code already handled), so the three sweeps go
  through `IlSamples.WithIl`.
- Criterion 6. `powershell-19687` (net8.0 on both sides), compare mode quick, `--jobs 4`, one run
  per column on 2026-10-08, exit 1 both. Before is `origin/main` `40057429`; after is this
  branch. Another session's benchmark and three other agents' builds ran on the box during
  both, so the wall-clock times (960 s and 734 s) say nothing.

  | | before | after |
  |---|---|---|
  | results | 33889 | 34183 |
  | EQ001 | 33855 | 34149 |
  | EQ002 | 6 | 6 |
  | EQ003 | 28 | 28 |
  | EQ004 / EQ005 / EQ006 | 0 / 0 / 0 | 0 / 0 / 0 |
  | `matchedPairs` | 33889 | 34183 |
  | `pairsCongruent` | 33852 | 34146 |
  | `pairsWholeBodyOpaque` | 369 | 663 |
  | `no-body` bodies, each side | 46 | 340 |
  | `changedPairs` | 37 | 37 |
  | results that list a new pair in `assumedCallees` | 0 | 217 |

  The 294 new results are the pair's `extern` methods, each `EQ001` with
  `proofMethod: congruence`: the pull request edited none of them. No result that was there
  before changed its rule id, its `proofMethod` or its `unknownReason`, and none left. 217
  results now name an `extern` pair among the callees they assume, where before no pair stood
  behind that call. None is in `unprovenAssumptions`, since all 294 are Equivalent. No `extern`
  method of the pair lacks both `[DllImport]` and `InternalCall`: that case would be an `EQ003`,
  and there is none.
- Not measured: a pair that crosses a runtime. There every matched `extern` method becomes an
  `EQ003` (ADR 0054 decision 4), so a migration's Unknown count rises by the number of its
  imports. P2-124 and P2-130 rerun those pairs.
- The scoreboard is not touched: no file under `docs/runs/` is added here.
