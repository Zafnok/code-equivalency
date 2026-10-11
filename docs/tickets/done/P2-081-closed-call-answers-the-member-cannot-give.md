# P2-081 A Divergent that rests on a BCL call's answer the real member cannot give is not EQ002
Status: done (PR #475)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-060

## Goal
ADR 0041 stops a closed call (a `string` or primitive member handed only strings and primitives)
from writing the heap. Its result and `threw` flag are still free functions that the solver
chooses. P2-060 measured what that costs. With the differential gate's rule 2 exemption narrowed to
open calls, the 5,000-pair nightly budget fails (seed `9v8KtBVEhjFh`, DropNullCheck under IL) on a
model in which `Nullable<int>.GetValueOrDefault()` throws. That call throws on one side before a
statement the other side runs first. A result works the same way: nothing stops the model from
having `String.Concat` return null. No real member does either, so every such result on real code
is a false EQ002. Decide, through
`equiv-adr`, how a Divergent that rests on a closed call's answer is kept out of EQ002. Two
candidates are a no-throw and non-null summary per member, and tainting a closed call's outputs in
the replay, which would amend ADR 0026. Then implement that.

## Spec references
ADR 0041, ADR 0026 and its two 2026-09-30 clarifications, ADR 0018, VERIFICATION-MODEL section 7
(rule 2).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. The gate pair at seed `9v8KtBVEhjFh` is pinned in `DifferentialSoundnessTests`. Its verdict is
   Unknown or Equivalent, or a Divergent that replays.
3. Rule 2's exemption covers only models whose run records an open call. Rules 1 to 3 hold on
   the PR and nightly budgets under both lowerings.

## Tests
- `DifferentialSoundnessTests.AClosedCallsAnswerIsOneTheMemberCanGive`
- the unit tests the decision names

## Out of scope
Open calls (they may still write the heap, ADR 0026's first clarification). Callee summaries for
anything but closed calls.

## Notes
- Decision (criterion 1, through `equiv-adr`): a new ADR, 0056. It amends ADR 0026's "ordinary `IrCall`
  results stay untainted" and changes when a result is EQ002, so it is not a clarification. Three parts.
  (1) The replay taints a closed call as it taints an `opaque:` call: result, `threw` flag, `ref`/`out` outputs
  and trace event. A model that differs only there is Unknown(Abstraction) with its candidate input.
  (2) The open calls of the two traces are also compared on their own, closed events left out.
  (3) A call to a member on a short list that never throws has no `threw` flag.
  The per-member summary alone was rejected: it leaves wrong values (`Concat(s, "t") == s`) and members that
  throw for some arguments, so rule 2 could not be strict.
- Why three parts and not one: part 1 alone was built first and measured on the samples. 8 results in 6
  samples went from EQ002 to EQ003 and `cleanup-modern-syntax` exited 0. Two shapes. One side fails before a
  call and the other goes on through one (`added-null-guard` `Greet`): the side past the call branched on a
  tainted `threw` flag, and part 3 removes that branch for `ToUpper` and `Concat`. An open call against a
  closed one (`repeated-edit`: `Convert.ToInt32(string)` against `int.Parse(string)`): the closed event at the
  first trace difference hid the open call, and part 2 compares the open calls apart. With all three, no
  result of the 45 samples changes its rule id.
- Decision: the list is read from the call identity (`ClosedCalls.NeverThrows(CallIdentity)`), not the symbol.
  An API-equivalence entry names its modern member only by identity; read from the symbol, the rewritten
  legacy `TrimEnd()` kept its flag while the modern call lost it, and `bcl-rebound-overloads` `TrimTail` went
  from Equivalent to Divergent.
- Decision: a closed call to a `RuntimeChanged` member is not tainted. Its two runtimes differing is the
  runtime-changes table's row, and tainting it turned `runtime-row-unaffected-overload` `HasCulture` from
  EQ006 to EQ003.
- Decision: the replay's taint predicate is per side (`ModelOracle.IsAbstraction`): an `opaque:` identity, or
  `ModelDecoder.IsClosedAbstraction`. `IrInterpreter` is unchanged. An identity that one call site leaves open
  is encoded open (ADR 0041), so it stays untainted and the gate still excuses it.
- Decision (criterion 3): the gate reads "open" from the lowered IR: a model is excused when its run records a
  callee that some `IrCall` of that side does not mark closed.
- `SsaBuilder` assumed every `IrCall` it renames has a `threw` flag; a flagless call crashed the lowering
  (`Value cannot be null. (Parameter 'key')`). Fixed there.
- Sample snapshots: 8 `expected.sarif.json` change, none in a rule id or an exit code. `il-fallback` `Add`:
  `removedFailures` is `none-proved`, was `unknown`. `bcl-overload-rebinding` `StripOther`,
  `bcl-rebound-overloads` `TrimTailOther` and `cleanup-modern-syntax` `Join`: a `newFailures: found` whose model
  had a BCL member throw is `none-proved`. `repeated-edit` (three results) and `forwarder-to-bcl` `Trimmed`:
  `divergenceKind` is `undetermined`, was `result-changed`, since the three queries' models are tainted.
  `added-null-guard` and `removed-null-check` `Greet`: another model of the same verdict.
- Three IR snapshots in `Equiv.Frontend.CSharp.Tests` lose the `threw` flag and branch of `Concat` and
  `Int32.ToString()`.
- The fixture `call-closed-divergent.ir` (`Abs(a)` against `Abs(a) + Abs(a)`) is now Unknown(Abstraction): it
  differs only for the answers the solver chose for `Abs`.
- Criterion 2: `AClosedCallsAnswerIsOneTheMemberCanGive` pins the first pair the seed draws (DropNullCheck over a
  lifted `&` and `GetValueOrDefault()`). On `main` at 1f9bb699 that pair is already a Divergent that replays under
  both lowerings: Z3 now picks a model in which the legacy side throws `IndexOutOfRangeException` at `u[0]`. So
  that test passes with and without the change, and the tests that fail without it are in `TaintReplayTests`
  and `ClosedCallsTests`.
- Still lost, by design: a divergence both sides reach only through a closed call's answer, and one that shows
  only past a closed call that can throw. Three tests pinned such a Divergent and now pin Unknown(Abstraction):
  `PreservingSeedDivergenceTests`' two string concatenations (renamed `...IsNeverEquivalent`), and
  `SetSsh_LowersToOneBodyOnceTheWrapperIsResolved` across runtimes, where both sides reach
  `SetEnvironmentVariable` only when `string.IsNullOrEmpty` answers false. `removed-null-check.execute.sarif`
  changes its model: the legacy side throws `ArgumentNullException` where the old model had a call throw
  `System.Exception`. P2-158 counts these on the corpus.
- Measured (criterion 3), with the final change: the whole integration project passes at the PR budget, and the
  gate passes the nightly budget at seed `9v8KtBVEhjFh` under both lowerings. At that seed
  `FloatingPointPairsAreDecidedAfterRefinement` finds 9 Divergent of 40 where it wants 10, with and without this
  change: its batch depends on the seed, and it passes at the default one.
- `Release: minor`: some pairs that were EQ002 are now EQ003.
