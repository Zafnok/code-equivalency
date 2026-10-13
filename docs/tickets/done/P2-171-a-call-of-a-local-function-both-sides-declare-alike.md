# P2-171 A call of a local function is not an opaque when both sides declare the same local function
Status: done (PR #491)
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-127

## Goal
P2-127 closed a false-Equivalent path: a call of a local function was a call of a shared name, so
a proof never read the local function's body. Since then the call is an opaque with reason
`LocalFunction`, shared only when the pair is congruent. P2-127's Notes name the cost: "a member
that calls a local function by name is Unknown even when the local function is the same on both
sides", and leave proving those to a later ticket. No open ticket is that one.

P2-110's quick run at equiv `78853202` measures the cost on a cleanup
(`docs/runs/2026-10-13-cleanup-gitextensions-11372/SUMMARY.md`): on `gitextensions-11372`
`LocalFunction` is in the reason set of 43 of 319 changed pairs (13.5%), the whole reason set of
20, and the first reason of 21 Unknowns. It is the largest opaque reason on that pair, and one of
the two things that take its solver-proved share from 75.2% on P2-120's branch to 68.0%. On
`gitextensions-11284` it is in 18 of 178 reason sets (10.1%). The pull requests rewrite a
collection initializer or a `var`; most of these members' local functions are untouched.

When done, a member that calls a local function is decided when the two sides' local functions can
be shown to be the same procedure or equivalent ones, and is still never Equivalent on a proof that
did not read them.

## Spec references
`docs/tickets/done/P2-127-*.md` (the Notes: the decision, the cost and the 26 lost Equivalents);
ADR 0018 (callees and congruence); ADR 0024 (fingerprints); VERIFICATION-MODEL sections 3 and 5;
`IOPERATION-COVERAGE.md` (where it says what a call of a local function lowers to); `.claude/skills/equiv-decide` and `equiv-adr` (what a
shared callee is allowed to rest on is close to an ADR's subject: run the bar test first).

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over `gitextensions-11372`'s 43 changed pairs with `LocalFunction` in the reason
   set, in how many the local functions the member calls have the same text on both sides, in how
   many they differ, and what they capture. Record the table in Notes.
2. Decide, with `equiv-decide` and the `equiv-adr` bar test, how a call of a local function is
   compared: as a matched pair of procedures with their captures as parameters, by a fingerprint of
   the local function's lowered body, or by inlining. Log the decision and why the P2-127 path
   stays closed under it.
3. A sample whose two sides declare the same local function and differ elsewhere in the member is
   Equivalent by a `proofMethod` other than `congruence`.
4. P2-127's samples stay as they are: a sample whose two local functions differ in behaviour is
   Divergent or Unknown, never Equivalent.
5. Rerun `gitextensions-11372` in compare mode quick. Notes record the reason-set count next to 43
   and the solver-proved count next to 217 of 319, and no pair is Equivalent whose local functions
   differ in text without a proof that read them.

## Tests
- `IrLowererTests`: a call of a local function under the new rule; P2-127's
  `ACallOfALocalFunctionIsNotACallOfASharedName` still holds or is replaced by its stronger form.
- An integration test per sample of criteria 3 and 4.
- A property test (CsCheck) that a change inside a local function's body is never reported
  Equivalent for the member that calls it unless the two bodies are.

## Size guard
Recursive local functions, or local functions that capture by reference a variable the member
writes after the call, may stay opaque: say which in Notes and stop there.

## Out of scope
Lambdas and method groups (`DelegateCreation`, P2-136's subject); static local functions that the
compiler already treats as ordinary methods need no special case beyond the rule decided here.

## Notes
- Filed 2026-10-13 from P2-110's rerun of the cleanup pairs. No open ticket owned the reason.
- Soundness comes first here: this ticket undoes part of what a soundness fix cost, so criterion 4
  and the property test are not optional.
- Criterion 1, the count. Method: every changed pair of P2-110's quick run (`78853202`; every result but a
  `congruence` EQ001 and an `unmatched-overload`, 319) was located in both checkouts by its result's line, and each
  side's member was read with Roslyn, one file at a time and without the solution's references. A pair counts when its
  own body, outside lambdas and local functions, calls a local function. "Same text" compares the tokens of every local
  function the member's calls reach, in order of first call. What a function captures is Roslyn's data flow over that
  one file, so `this` is exact and a local's type is not needed. 44 pairs call a local function. The census counts 43
  with `LocalFunction` in the reason set; the one pair of difference was not looked for.

  | local functions the member calls | pairs | of them Unknown `opaque` / `timeout` in P2-110's run |
  |---|---|---|
  | same text on both sides | 30 | 18 / 12 |
  | differ in text | 14 | 12 / 2 |

  What the functions of a pair capture, a pair counted once per row it fits:

  | | same text (30) | differ (14) |
  |---|---|---|
  | nothing: `static`, or neither a local nor `this` | 9 | 5 |
  | `this` only | 9 | 7 |
  | a local or parameter, read only | 11 | 2 |
  | a local or parameter the function writes | 1 | 0 |
  | has a `ref` or `out` parameter | 3 | 0 |
  | recursive | 1 | 0 |
  | an iterator | 0 | 1 |
  | reaches another local function | 9 | 2 |

  So the pull request rewrote an expression inside a local function in 14 of the 44, and in the other 30 the functions
  are untouched, as the Goal expected of most.
- Bar test (`equiv-adr`): first row. ADR 0024 decision 2 already says what a fragment both sides hold is, and its
  clarification of 2026-10-01 only says a fragment that refers to an outer local function has no fingerprint. The call
  itself is a case it did not spell out. Vehicle: a dated bullet under ADR 0024's Clarifications in this PR, flagged in
  the PR description. No verdict's meaning, rule id or SARIF shape changes, and no component boundary: the change is
  inside `Equiv.Frontend.CSharp`.
- Decision: how a call of a local function is compared -> a shared fragment (ADR 0024 decision 2) whose fingerprint is
  that of the bound code the call runs (the called function's declaration and every local function it reaches, with the
  call's type arguments), reading the call's arguments, lowered as any call's, and then the variables that code
  captures. Alternatives: a matched pair of procedures with the captures as parameters; inlining the function's graph at
  the call; a fingerprint of the function's lowered IR. Rule: 4, after 1 (it is exactly the `IrOpaque` fingerprint and
  reads the encoder already shares, so nothing in `Equiv.Core` or `Equiv.Verify.Z3` changes). The count says it reaches
  the 30 pairs whose functions are untouched. Inlining is what the other 14 need and is P2-178. A fingerprint of the
  lowered IR was not taken because a local function's body is not lowered at all today, so it needs most of inlining
  first.
- Why P2-127's path stays closed under it: the proof reads the body through the fingerprint. Two functions that differ
  in any bound operation, their own or one of a function they reach, have two fingerprints, and a fragment one side
  holds alone keeps ADR 0014's meaning, so the pair is Unknown(Opaque) as it is today. P2-127's Notes rejected a
  fingerprinted callee because "a local function reads and writes the locals it captures, which a call's arguments and
  heap pairs do not carry". Here the reads carry them, and a call whose function writes one has no fingerprint.
- Decision: which captured variables make a call unshared when stored after it -> all of them. Alternatives: only those
  a lambda, an iterator or an `async` function inside the called function can still read after it returns. Rule: 4, and
  it is the Size guard's own allowance. A function with none of the three runs to its end inside the call, so the
  narrower rule is sound too; it was left out because its soundness rests on listing every way a frame outlives a call.
- Decision: a call with a `ref` or `out` argument, and one whose function writes a captured variable -> the bare opaque
  it was. Alternatives: outputs on a shared fragment, as `IrCall.RefOuts`. Rule: 4. `IrOpaque` has no outputs but its
  result, so this is an IR change (`equiv-extend-ir`) for 4 of the 30 pairs; P2-178 owns it with the rest.
- Size guard: a recursive local function is not left opaque. Its declaration is in the text once, and the argument
  that two calls with one fingerprint are one function does not depend on the code terminating. A capture the member
  stores after the call does leave the call opaque, as the guard allows.
- The compiler rejects a local function that names a struct's `this` (CS1673) or a `ref` local (CS8175), so the two
  exclusions a fragment has for those have no counterpart here.
- `FragmentLoweringTests.ANestedFromClauseLowersWithoutThrowing` asserted that the call's opaque is the one with no
  fingerprint (P2-127's Notes); every opaque of that body has one now.
- Criterion 5: `gitextensions-11372`, compare mode quick, no `--jobs` (24 at a time), this branch on `0a2f9a03`:
  252 s, exit 1, no crash (no `run.properties.unverified`). Before is P2-110's run at `78853202` (259 s), which
  differs from `0a2f9a03` in documents only.

  | of 319 changed pairs | before | after |
  |---|---|---|
  | `LocalFunction` in the reason set (census) | 43 | 43 |
  | results that name a `LocalFunction` opaque | 26 | 13 |
  | first reason `opaque:LocalFunction` (review list) | 21 | 10 |
  | proved Equivalent by the solver | 217 (68.0%): bounded 153, lockstep-induction 64 | 234 (73.4%): bounded 161, lockstep-induction 73 |
  | Divergent | 1 | 1 |
  | Unknown | 101: opaque 69, timeout 26, abstraction 4, unaligned-loop 1, recursion 1 | 84: opaque 59, timeout 19, abstraction 4, unaligned-loop 1, recursion 1 |

  The census's reason set does not move, and is not expected to: it counts an opaque by its reason whether or not
  the encoder shares it, as it counts a shared `DelegateCreation`. The two rows under it are the ones that say what a
  pair is Unknown for.
- Every result that changed is one of the 44 pairs that call a local function; the other 14570 results have the rule
  and the `proofMethod` or `unknownReason` they had. 20 changed. 17 are new Equivalents, all among the 30 whose
  functions have the same text: 11 were `opaque` and 6 `timeout` (a query with one call in place of an opaque is a
  smaller one). The other 3 moved between `opaque` and `timeout` and are still Unknown.
- No pair whose local functions differ in text is Equivalent: of the 14, 13 are `opaque` (12 name `LocalFunction`)
  and 1 is `timeout`, as before but for one that moved from `timeout` to `opaque`. The one Divergent is
  `BugReporter.Program::Main()`, as in every run of this pair.
- What is left of the 30 with the same text: 17 Equivalent, 6 `timeout`, 7 `opaque`, of which one names
  `LocalFunction` (`GitCommands.GitModule::ParseGitBlame(string,System.Text.Encoding)`, whose function writes a
  local it captures) and six name another construct. The 12 that differ and that one are P2-178.
- `gitextensions-11284` (18 of 178 reason sets) was not rerun; the criterion names one pair.
- No `docs/runs` summary and no scoreboard change: the criterion asks for these Notes, and the README's cleanup
  numbers stay P2-110's until a run is written up.
- The full `Equiv.Tests.Integration` project did not finish on this box, twice: the test process sat idle waiting on
  a child it had started, first an `equiv mcp` (`McpIntegrationTests`) and, with that class left out, an MSBuild
  build host of a test that loads `samples/`, in a shell with no console. Whether `main` does the same there was not
  checked. Run locally and green: all of `Equiv.Frontend.CSharp.Tests`, and of the integration project
  `LocalFunctionCallTests`, `DifferentialSoundnessTests`, `SharedFragmentTests`, `PairGenTests`,
  `ConditionSoundnessTests` and `CongruenceSoundnessTests`. CI runs the rest.
