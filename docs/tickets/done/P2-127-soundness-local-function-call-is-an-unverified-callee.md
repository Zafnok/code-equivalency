# P2-127 Soundness: a call of a local function is an ordinary call of a callee nothing verifies, so two different local functions prove Equivalent
Status: done (PR #402)
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-079

## Goal
A default run (no flag) reports a false Equivalent. `IrLowerer.Invoke` lowers a call of a local function
as an `IrCall` of the identity `<Type>::<LocalName>(<parameters>)`, the same on both sides. A callee's
change is normally caught by the callee's own pair (ADR 0019), but a local function is never a matched
procedure (`ProcedureEnumerator`), so nothing reads its body. Two members that differ only inside a
local function lower to the same IR and prove.

Repro (found by P2-079's closure pairs, then confirmed with `compare` on two .NET Framework 4.8
projects that differ only in the local function):

```csharp
public sealed class Holder
{
    private int _x;
    public void Bump()
    {
        int L(int v) => v + 1;   // modern: v + 2
        _x = L(_x);
    }
}
```

`Bump` is `EQ001`, `proofMethod: bounded`. Both sides lower to
`%r = call "Holder::L(int)"(%x)`. The pair is not congruent (the bound fingerprints differ, since
`BoundSerialiser` reads the local function's body), so the solver is asked, and the IR is the same.

The delegate forms are not affected: a lambda, and a local function converted to a delegate, are
`delegate:<fingerprint>` of the bound body or an unshared opaque (P2-067, M3-015).

## Spec references
ADR 0019 (a verdict lists the callees it assumed), ADR 0024 decision 2 (shared opaque fragments),
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (`Invoke`),
`src/Equiv.Frontend.CSharp/Fingerprinting/FragmentFingerprinter.cs` (a fragment that calls a local
function declared outside it has no fingerprint), `docs/tickets/IOPERATION-COVERAGE.md` (`Invocation`).

## Acceptance criteria (all must hold; nothing beyond them)
1. The repro above is a test in `tests/Equiv.Frontend.CSharp.Tests` (the two sides do not lower to the
   same IR, or lower to one with an opaque the other side does not share) and an integration test
   (`compare` on the pair does not report `EQ001` for `Bump`). Both fail before the fix.
2. A call of a local function is not an `IrCall` of an identity both sides share by name alone. Decide
   with `equiv-decide` between an opaque with reason `LocalFunction` (shared only on
   `FragmentFingerprinter`'s terms, which already serialise the local function's bound body when it is
   declared inside the fragment) and a callee identity that carries the fingerprint of the local
   function's bound body. Log the choice as a `Decision:` line.
3. `DifferentialSoundnessTests.PairsThatDifferOnlyInsideAClosureAreSoundUnderTheIlLowering` becomes
   `...UnderBothLowerings` and runs under both. P2-079 left it on the IL lowering alone, because rule 1
   fails under the IOperation lowering until this ticket (CsCheck seed `0000SiT-0iY1` at P2-079's
   commit).
4. `IOPERATION-COVERAGE.md`'s `Invocation` row says what criterion 2 decided.
5. The public-corpus pairs whose verdict changes are counted in `## Notes`, from one `full` run of
   `gitextensions-8522` before and after.

## Files
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`, its tests, an integration test for criterion 1,
`tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
`IrLowererTests.ACallOfALocalFunctionIsNotACallOfASharedName`,
`DifferentialSoundnessTests.PairsThatDifferOnlyInsideAClosureAreSoundUnderBothLowerings`, and the
integration test for criterion 1.

## Size guard
Lowering a local function's body into its caller so that such pairs can be proved is not this ticket.
Here they only stop being proved wrongly.

## Out of scope
The IL lowering, which P2-079 fixed. Inlining local functions.

## Notes
- Found 2026-10-03 by P2-079's `PairGen.ClosurePair`: rule 1 failed under the IOperation lowering on a
  pair whose local function read `v - 5` on one side and `v - 6` on the other.
- Until this lands, an Equivalent verdict on a member that calls a local function by name does not
  cover the local function's body.
- Decision: what a call of a local function lowers to -> an opaque with reason `LocalFunction`, shared only on
  `FragmentFingerprinter`'s terms (so never, for a call in the member's own body: the local function is declared outside
  the call). Alternatives: a callee identity that carries the fingerprint of the local function's bound body. Rule: 4.
  The fingerprinted identity is the larger change and is not sound by the fingerprint alone: a local function reads and
  writes the locals it captures, which a call's arguments and heap pairs do not carry, so `int a = 1; int L() => a;` and
  the same with `a = 2` would be one call. Making it sound means passing the captures and writing them back, which is
  most of the way to the inlining the Size guard excludes.
- The cost: a member that calls a local function by name is Unknown (abstraction, `opaque LocalFunction`) even when
  the local function is the same on both sides, unless the pair is congruent. Proving those is the inlining ticket.
- Criterion 5: one `full` run of `gitextensions-8522` before (`2627bf9f`, 11597 s) and after (the same commit with this
  fix, 10661 s), both exit 1, the two run at the same time on one box. 13742 results each; 35 changed rule:
  | before | after | results | names `LocalFunction` |
  |---|---|---|---|
  | EQ001 (bounded 21, lockstep-induction 5) | EQ003 `opaque` | 26 | yes |
  | EQ006 | EQ003 `opaque` | 3 | yes |
  | EQ001 (bounded) | EQ003 `timeout` | 4 | no |
  | EQ003 `abstraction` | EQ006 | 2 | no |

  By rule: EQ001 12757 -> 12727, EQ003 575 -> 606, EQ006 207 -> 206; EQ002 21, EQ004 15 and EQ005 167 unchanged. Another
  13 results were EQ003 before and are EQ003 naming `LocalFunction` now. The 29 that name it are this ticket's. Of the
  4 timeouts, `GitUI.BranchTreePanel.RepoObjectsTree::.ctor()` and `GitUI.CommitInfo.CommitInfo::ReloadCommitInfo()`
  call a local function, so their query changed; the other 2, and the 2 `abstraction` -> EQ006, do not name one and
  were not attributed (two runs sharing the box's solver time).
- Not checked: in how many of the 26 lost Equivalents the two sides' local functions really differ. Each was a proof
  that did not read the local function's body, whichever way that count falls.
- The frontend's `FragmentLoweringTests.ANestedFromClauseLowersWithoutThrowing` asserted every opaque of a body that
  calls a local function has a fingerprint; the call's opaque is now the one that has none.
- CI on this PR also failed once on `SecondSolverPrintTests.Z3AnswersTheRewrittenTextAsItAnswersTheOriginal`
  (CsCheck seed `b1l7BGrSxcc1`), a Z3 test this change does not reach; it passed on the re-run. No ticket yet.
