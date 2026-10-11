# P2-081 A Divergent that rests on a BCL call's answer the real member cannot give is not EQ002
Status: in-progress
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
  results stay untainted" and changes when a result is EQ002, so it is not a clarification. The replay
  taints a closed call as it taints an `opaque:` call: result, `threw` flag, `ref`/`out` outputs and trace
  event. A model that differs only there is Unknown(Abstraction) with its candidate input. The per-member
  summary was rejected as the whole answer: it leaves wrong values (`Concat(s, "t") == s`) and members
  that throw for some arguments. Its no-throw half is filed as P2-158, to win back divergences that
  show after a member that never throws.
- Decision: the replay's taint predicate is per side (`ModelOracle.IsAbstraction`): an `opaque:` identity, or one
  `TraceEncoder.IsClosed` says the encoding closed. `IrInterpreter` is unchanged. An identity that one call site
  leaves open is encoded open (ADR 0041), so it stays untainted and the gate still excuses it.
- Decision (criterion 3): the gate reads "open" from the lowered IR: a model is excused when its run records a
  callee that some `IrCall` of that side does not mark closed.
- Measured (criterion 3). With the exemption narrowed and no other change, the 200-pair budget at seed
  `9v8KtBVEhjFh` fails rule 2 under IL (FlipComparison; both sides throw `OverflowException` in C#; the only calls
  are members of `Nullable<T>`). With ADR 0056 it passes, and so does the nightly budget at that seed: 5,000
  generated pairs and 1,250 closure pairs, both lowerings, 11 minutes locally.
- Criterion 2: `AClosedCallsAnswerIsOneTheMemberCanGive` pins the first pair the seed draws (DropNullCheck over a
  lifted `&` and `GetValueOrDefault()`). On `main` at 1f9bb699 that pair is already a Divergent that replays under
  both lowerings: Z3 now picks a model in which the legacy side throws `IndexOutOfRangeException` at `u[0]`. So the
  test passes with and without the change, and the tests that fail without it are the five new ones in
  `TaintReplayTests`. With the change the pair is Unknown(Abstraction) from IL, naming `Nullable<int>`'s `.ctor`,
  `get_HasValue` and `GetValueOrDefault`, and still Divergent from IOperation.
- Surprise: that IL verdict is the cost ADR 0056 names, on a real divergence. The modern side reaches the closed
  calls before it can differ, so its outcome is tainted. P2-158's no-throw list would not win it back: a lifted
  operator branches on `get_HasValue`'s result, which stays tainted. `Nullable<T>`'s members need their real
  meaning for that, which no ticket covers yet.
- The fixture `call-closed-divergent.ir` (`Abs(a)` against `Abs(a) + Abs(a)`) is now Unknown(Abstraction): it
  differs only for the answers the solver chose for `Abs`.
- Stopped here (2026-10-11), before the samples were touched. ADR 0056's cost is larger than the ADR says.
  `SamplesEndToEndTests` on this branch: 8 results in 6 samples go from EQ002 to EQ003 (abstraction), and each is
  a real divergence a reader would call one. By the closed callee the candidate names:
  `added-null-guard` `Greet` (`String.ToUpper()`, `String.Concat(string,string)`); `repeated-edit` `Quantity`,
  `Total`, `IsBulk` (`Int32.Parse(string)`); `cleanup-modern-syntax` `Join` (`String.Concat(string,string)`);
  `forwarder-to-bcl` `Trimmed` (`String.IsNullOrWhiteSpace(string)`); `bcl-rebound-overloads` `TrimTailOther`
  (`String.TrimEnd(char)`); `runtime-row-unaffected-overload` `HasCulture`
  (`String.IndexOf(string,StringComparison)`). `cleanup-modern-syntax` then exits 0 where its README says 1.
  Two shapes. One side fails before a call and the other goes on through one (`Greet`): a no-throw list (P2-158)
  gives that back. Both sides use the call's result (`Parse(s) * 2` against `Parse(s) * 3`): nothing sound gives
  that back, because it is a divergence only if `Parse` returns non-zero for some string, and a string in a model
  is an element with no text. Criterion 3 (rule 2 strict for closed calls) needs the taint, and the taint costs
  these. The snapshots, READMEs and named sample tests are not updated; the gates are red on this branch.
  The other 51 failures of that class in the local run are `FileNotFoundException` on the SARIF file: samples
  that do not load in this worktree, not this change.
- `Release: minor`: some pairs that were EQ002 are now EQ003.
