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
The replay taints a closed call as it taints an `opaque:` call (ADR 0026): its result, its `threw`
flag, its `ref` and `out` outputs and its trace event are tainted, and taint spreads from them by
ADR 0026's rules. "Closed" is the encoder's notion (ADR 0041): an identity every call to which in the
product is closed. A model whose runs differ only in observables tainted this way is
Unknown(Abstraction). It carries the model as `properties.candidateCounterexample` and names the
callee in `properties.abstractions`, as it would a pure function. The encoding does not change, so
no Equivalent is gained or lost. The differential gate's rule 2 excuses a replay that does not
diverge only when the model's run records an open call.

## Why
- It is the only candidate that covers every answer. A closed member has one real behaviour, and
  the replay does not know it. Whatever the solver picked may be wrong: a throw from a member that
  never throws, a null from one that never returns null, or simply the wrong value
  (`Concat(s, "t") == s`, `int.Parse("12")` throwing). A divergence that is untainted does not depend
  on any of them (ADR 0026's second Why), so every real behaviour of the member reproduces it.
- ADR 0026 kept ordinary call results untainted because "a divergence can only depend on a call
  result after the traces already differ". Its two 2026-09-30 clarifications found that false. For
  an open call the arbitrariness is still the modular assumption: the callee may be user code, and
  what it does is a premise the verdict names (ADR 0019). A closed callee is a fixed BCL member that
  no pair of the run verifies, so nothing stands behind the solver's choice for it.
- The event is tainted as well as the outputs. A closed callee writes no heap and runs no user code
  (ADR 0041), so one more closed call on one side shows in a real run only if it throws, and whether
  it throws is the solver's choice. The gate's C# observables cannot see the event at all, so a
  Divergent that rested on it could never replay.
- Treating it exactly as an `opaque:` call needs no new kind of taint. `IrInterpreter` and its
  taint predicate are unchanged; the backend's predicate answers true for one more set of callees.
- Taint exists only in the replay, so the change can turn a Divergent into an Unknown and nothing
  else. A candidate input on an Unknown costs a reviewer less than a false EQ002 costs the gate.

## Rejected
- **A no-throw and non-null summary per member, alone.** It removes the measured models and leaves
  the wrong-value ones, and the members that throw for some arguments. Rule 2 could not be made
  strict for closed calls, and each entry needs review (ADR 0043 accepted that for a short list,
  ADR 0041 rejected it for heap summaries).
- **Tainting the outputs and leaving the event untainted.** It needs a second kind of taint in
  Core's interpreter, and it keeps Divergents that rest on one side making an extra closed call,
  which no C# run can show.
- **Running the real member on the model's arguments.** The replay would call into the BCL by
  reflection, on the tool's runtime and culture and not the pair's, which for a legacy side is
  another framework.
- **Tainting every call.** ADR 0026 rejected it, and the ticket rules open calls out. An open
  callee may be the user's code, compared as its own pair.

## Consequences
- EQ002 is again a claim that holds whatever a closed BCL member really does. Rule 2 of the gate is
  strict for every model that records no open call, under both lowerings.
- Precision cost: a side that makes a closed call then branches on its tainted `threw` flag, so the
  rest of that side is tainted (ADR 0026). A real divergence that shows only after a closed call is
  now Unknown(Abstraction) with its candidate input, and `failureRefinement`'s answers there are
  `unknown`. The cost on the corpus is not measured here; the next corpus run shows it as Divergents
  that became Unknown(Abstraction) naming a closed callee. Ticket P2-158 takes up the summary this ADR
  rejects as the whole answer: a closed member known never to throw has no `threw` flag, which gives
  back the divergences that show after it without using its result.
- `properties.abstractions[].identity` can now be a closed call's identity. The field's shape does not
  change. An EQ002 on some pairs becomes an EQ003, so the implementing commit carries `Release: minor`.
- VERIFICATION-MODEL sections 6 (the replay rule) and 7 (rule 2) change in the PR that accepts this
  ADR. ADR 0026 and ADR 0041 each gain a clarification that points here.
- Ticket P2-081 implements it and pins the gate's pair.
