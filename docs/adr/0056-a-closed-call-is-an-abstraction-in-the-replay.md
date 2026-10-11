# ADR 0056: In the replay a closed call is an abstraction, so no Divergent rests on what the solver chose for it

Status: accepted (2026-10-11). Amends ADR 0026's "ordinary `IrCall` results stay untainted" for closed
calls only, and ends the rule 2 exemption ADR 0041 kept for them.

## Context
ADR 0041 stops a closed call from writing the heap. Its result, `threw` flag and `ref`/`out` outputs
are still functions the solver chooses, and ADR 0026 leaves them untainted. So a Divergent can rest
on an answer the real member never gives (ticket P2-081). P2-060 measured it: with the differential
gate's rule 2 exemption narrowed to open calls, the nightly budget fails at seed `9v8KtBVEhjFh` on a
model in which `Nullable<int>.GetValueOrDefault()` throws. Narrowed again for this ticket, the
200-pair budget at that seed fails under IL on FlipComparison: both sides throw the same
`OverflowException` in C#, and the pair's only calls are to members of `Nullable<T>`. Nothing
stops a model from having `String.Concat` return null, or its own first argument, either. On real
code each of these is a false EQ002.

## Decision
1. **The replay taints a closed call as it taints an `opaque:` call** (ADR 0026): its result, its
   `threw` flag, its `ref` and `out` outputs and its trace event are tainted, and taint spreads from
   them by ADR 0026's rules. "Closed" is the encoder's notion (ADR 0041): an identity every call to
   which in the product is closed. A model whose runs differ only in observables tainted this way
   is Unknown(Abstraction). It carries the model as `properties.candidateCounterexample` and names
   the callee in `properties.abstractions`, as it would a pure function. A closed member the
   runtime-changes table lists (`CallIdentity.RuntimeChanged`) is not tainted: that its two runtimes
   answer differently is the table's row, which an EQ006 reports.
2. **The open calls of the two traces are also compared on their own.** With the closed events left
   out, the first open event that differs is a real difference when it is untainted on both sides.
   An untainted open event is also a real difference, whatever the other side's taint, when the
   other side's procedure makes no open call to that event's callee and holds no shared fragment:
   whichever path that side takes, its event there is another one or none. This applies only when
   closed calls are the only abstractions the other side's run reached, so a pair that a pure
   function or a shared fragment made Unknown stays as ADR 0026 left it.
3. **A call to a member that never throws has no `threw` flag.** The frontend holds one short list
   of members that throw for no argument, a non-null receiver given, read by call identity in both
   lowerings: `String.Concat` of two to four strings, `String.IsNullOrEmpty`,
   `String.IsNullOrWhiteSpace`, `String.ToUpper()`, `ToLower()`, `ToUpperInvariant()`,
   `ToLowerInvariant()`, `Trim`, `TrimStart` and `TrimEnd` with no argument or one `char`, the
   parameterless `ToString` of `bool`, `char` and the integers, and `Nullable<T>`'s constructor,
   `HasValue` and `GetValueOrDefault`.

The encoding of a call that keeps its flag does not change. The differential gate's rule 2 excuses a
replay that does not diverge only when the model's run records an open call.

## Why
- Decision 1 is the only candidate that covers every answer. A closed member has one real
  behaviour, and the replay does not know it. Whatever the solver picked may be wrong: a throw from
  a member that never throws, a null from one that never returns null, or simply the wrong value
  (`Concat(s, "t") == s`, `int.Parse("12")` throwing). A divergence that is untainted does not depend
  on any of them (ADR 0026's second Why), so every real behaviour of the member reproduces it.
- ADR 0026 kept ordinary call results untainted because "a divergence can only depend on a call
  result after the traces already differ". Its two 2026-09-30 clarifications found that false. For
  an open call the arbitrariness is still the modular assumption: the callee may be user code, and
  what it does is a premise the verdict names (ADR 0019). A closed callee is a fixed BCL member that
  no pair of the run verifies, so nothing stands behind the solver's choice for it.
- The event is tainted as well as the outputs. A closed callee writes no heap and runs no user code
  (ADR 0041), so one more closed call on one side shows in a real run only if it throws, and whether
  it throws is the solver's choice. The gate's C# observables cannot see the event at all.
- Decision 1 alone costs too much, and decisions 2 and 3 exist to pay it back soundly. Measured on
  the samples with decision 1 only: 8 results in 6 samples went from EQ002 to EQ003, and
  `cleanup-modern-syntax` exited 0. Each side branches on a closed call's tainted `threw` flag, so
  everything after any closed call was tainted, and a closed event at the first trace difference
  hid an open call behind it.
  - Decision 3 removes the branch where there is nothing to branch on. `added-null-guard`'s `Greet`
    (legacy throws on null, modern goes on through `ToUpper` and `Concat`) is Divergent again.
  - Decision 2 keeps what ADR 0018 already says is observable. `Convert.ToInt32(string)` (open: its
    type is not inert) against `int.Parse(string)` (closed) differs in an open call whatever `Parse`
    answers, and `repeated-edit` keeps its three Divergents.
  - With all three, no result of the 45 samples changes its rule id.
- The list is by identity, not by symbol, because an API-equivalence entry (ADR 0020) names its
  modern member only by identity. Read from the symbol, a rewritten legacy call kept its flag while
  the direct modern call lost it, and `bcl-rebound-overloads`' `TrimTail` went from Equivalent to
  Divergent.
- Dropping a flag is sound only if the member really never throws, so the list is short and each
  entry is a claim someone can check, as ADR 0043's are. It also helps proofs: two sides no longer
  have to agree on a throw that cannot happen.
- Taint exists only in the replay, so decisions 1 and 2 can turn a Divergent into an Unknown and
  nothing else. A candidate input on an Unknown costs a reviewer less than a false EQ002 costs the
  gate.

## Rejected
- **A no-throw and non-null summary per member, alone.** It removes the measured models and leaves
  the wrong-value ones, and the members that throw for some arguments. Rule 2 could not be made
  strict for closed calls.
- **Decision 1 alone.** The measurement above.
- **Tainting the outputs and leaving the event untainted.** It needs a second kind of taint in
  Core's interpreter, and it keeps Divergents that rest on one side making an extra closed call,
  which no C# run can show. Decision 2 gets the precision back without it.
- **Running the real member on the model's arguments.** The replay would call into the BCL by
  reflection, on the tool's runtime and culture and not the pair's, which for a legacy side is
  another framework. A string in a model is also an element with no text.
- **Tainting every call.** ADR 0026 rejected it, and the ticket rules open calls out. An open
  callee may be the user's code, compared as its own pair.
- **A rule for "never throws" (every parameterless member of an inert type).** `Nullable<T>.Value`
  and `String.Normalize()` throw. ADR 0043 rejected the same rule for the same reason.

## Consequences
- EQ002 is again a claim that holds whatever a closed BCL member really does. Rule 2 of the gate is
  strict for every model that records no open call, under both lowerings.
- What is still lost: a divergence both sides reach only through a closed call's answer, and one
  that shows only past a closed call that can throw. Each is Unknown(Abstraction) with its candidate
  input, naming the callee. Three tests that pinned such a Divergent now pin the Unknown:
  `c + " " + a` against `a + c + " "` on strings (it differs only in what `Concat` returns), the same
  over two properties, and Git Extensions' `SetSsh` across runtimes (both sides reach
  `Environment.SetEnvironmentVariable` only when `string.IsNullOrEmpty` answers false). All three do
  differ on real inputs; that is a fact about the member which the replay does not have. Giving the
  commonest such members their meaning is the way back, and P2-159 finds which they are. On the samples
  this shows as `failureRefinement` answers and `divergenceKind`s that were `found` on a model in
  which a BCL member threw and are now `none-proved` or `unknown`: 8 snapshots change, in those
  properties and in one model. The cost on the corpus is not measured here; the next corpus run
  shows it as Unknown(Abstraction) results naming a closed callee, which ticket P2-159 counts.
- `IrCall.Threw` was already optional; a listed member's call now has none. `IrInterpreter`, the
  encoder and the IR text format are unchanged.
- `properties.abstractions[].identity` can now be a closed call's identity. The field's shape does not
  change. An EQ002 on some pairs becomes an EQ003, so the implementing commit carries `Release: minor`.
- VERIFICATION-MODEL sections 1 (the list), 6 (the replay rule) and 7 (rule 2) change in the PR that
  accepts this ADR. ADR 0026 and ADR 0041 each gain a clarification that points here.
- Ticket P2-081 implements it and pins the gate's pair.
