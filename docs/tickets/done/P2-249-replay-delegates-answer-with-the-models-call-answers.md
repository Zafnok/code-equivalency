# P2-249 Execution reach: a replay builds a delegate argument the model's runs call, answering each call as the model does
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-245

## Goal
ADR 0062 decision 3 (as P2-245 accepted it): a replay builds a delegate argument where neither of the model's runs calls
through it, and is `not-constructible` ("the model's runs call `<parameter>`, and the counterexample holds none of the
model's answers to that call") where one does, because the `Counterexample` a replay is given holds the inputs and the
two runs' outcomes and call traces, not the answers the solver's model gave the calls in them. The answers exist only in
`Equiv.Verify.Z3`'s `ModelDecoder.ModelOracle` while the model is decoded, and P2-245's size guard forbids any change in
`Equiv.Verify.*`. When done, the counterexample carries the model's answer to each open call of a delegate argument
(its result or its `threw` flag, per argument tuple), and a replay builds that delegate as a lookup of the call's
arguments in the table, answering the model's answer, or the model's default for a tuple the table does not hold, and
throwing one fixed exception type where the model's answer is `threw`. Nothing changes a verdict, a rule id, a
fingerprint or an exit code (ADR 0035).

## Spec references
ADR 0062 decision 3, ADR 0035 decision 2, ADR 0019 (an open call's answer is the modular assumption, the same on both
sides), ADR 0026, ADR 0056, VERIFICATION-MODEL section 6 (the replay paragraph), `src/Equiv.Verify.Z3/ModelDecoder.cs`
(`ModelOracle`, `ReplayBoth`), `src/Equiv.Core/Verdicts/Counterexample.cs`, `src/Equiv.Frontend.CSharp/Execution/ModelObjects.cs`
(`Delegate`), `DriverHeap.Delegate`, `docs/runs/2026-10-19-execution-reach.md` (the count below).

## Acceptance criteria (all must hold; nothing beyond them)
1. A `Counterexample` decoded from a solver model carries, for every open call whose callee is a delegate's `Invoke` and
   whose receiver is an input, the model's answer per argument tuple the run made (its result, or its `threw`), read
   from the model the decoder already holds and never re-asked; the counterexample's equality, its SARIF `model` text
   and every fingerprint stay as they are.
2. A replay builds such a delegate argument as a generated function of its type that answers each call from that table,
   the model's default for a tuple the table does not hold, and throws one fixed exception where the answer is
   `threw`; `replayInputs` says `delegate`. The reason "the model's runs call ..." is left only where an answer is not
   one a driver builds (an object result, a `ref` or `out` output, a heap write), each counted.
3. Rerun the pairs whose replays P2-245 counted under that reason (`docs/runs/2026-10-19-execution-reach.md`) and give
   each one's replay value now. A `not-reproduced` gets a ticket (ADR 0035 decision 2).

## Design
- The answers belong to the counterexample, not to the frontend: record them where `ModelOracle.Answer` computes them,
  keyed by callee identity and argument values, and hand them to `Counterexample` as a plain IR table in Core.
- A delegate is called through its `Invoke` with the delegate as the first argument (the lowering's instance call), so
  the table's key is the delegate element and the remaining arguments.

## Files
`src/Equiv.Verify.Z3/ModelDecoder.cs`, `src/Equiv.Core/Verdicts/Counterexample.cs` (or a record beside it),
`src/Equiv.Frontend.CSharp/Execution/ModelObjects.cs`, `DriverHeap.cs`, their test files; `docs/VERIFICATION-MODEL.md`'s
replay paragraph; the run file the rerun writes.

## Tests
Unit tests: the decoder records a delegate call's answer and only that; a replay's delegate answers a recorded tuple,
the default for another, and throws for a `threw` answer; an integration test against a pair whose Divergent passes a
delegate the model calls, reproduced on both runtimes.

## Out of scope
Answers of calls that are not a delegate argument's (ADR 0026 keeps them the modular assumption); `ref`/`out` outputs and
heap writes of a delegate call.

## Notes
- Filed 2026-10-19 by P2-245, whose size guard forbids the `Equiv.Verify.*` change this needs (ADR 0062 decision 3 as
  accepted).
- Decision: the decoder takes a call as a delegate input's when its identity's member is `::Invoke(` and its first
  argument is an element the inputs hold (directly, or in an input map). Alternative: a delegate flag on `IrCall`, set
  by the lowering. Rule: 2 (the IR has no such flag, and adding one touches every layer for a fact the identity
  already carries; a non-delegate method named `Invoke` is recorded and never read, since a replay reads the table only
  for a delegate it builds).
- Decision: the answers ride on `Counterexample.Answers` (Core `CallAnswer`), an `init` property outside the record's
  equality, so the counterexample's equality, `CounterexampleText` and every fingerprint are unchanged. Rule: criterion 1.
- Deviation: "the model's default for a tuple the table does not hold" is the return type's default (`null` for a void
  delegate). The model's function also takes the call's position and the heap (ADR 0018), so its `else` is not a
  function of the arguments and Z3 may give it as a term over them; no run of the model makes such a tuple, so any
  answer is the model's, as ADR 0062 decision 3 already says of a delegate the runs never call.
- Decision: one argument tuple the model answers two ways (two positions, or the two sides) is `not-constructible`
  ("the model's runs call f twice on (...) and the model answers them differently, which no function of its arguments
  does"). Alternative: answer in call order, a stateful delegate. Rule: the ticket's Design (a lookup of the call's
  arguments) and ADR 0019 (one table on both sides).
- Decision: a delegate a run of the model hands to another call (any trace event that carries it and is not one of its
  recorded `Invoke` calls) is `not-constructible` with its own reason ("the model's runs hand f to <callee>, which may
  call it in ways the model does not give"), where P2-245 said "the model's runs call f". Rule: soundness first (that
  callee's calls of it are not the model's).
- Decision: an object result is built when the model makes it null or it is an object an input holds (built or
  referred to as replay builds that input); any other object result is "the model's answer is an object". A key
  argument that is an object compares by reference to the object the case built. Rule: criterion 2 (an object result
  is left only where no driver builds it).
- Decision: the fixed exception is `System.Exception`, the type the lowering gives an open call's throw
  (`IrLowerer.ThrowIf(..., known: false)`), so a `catch` of a narrower type does not take it. Rule: 1 (mirror the
  lowering).
- Decision: a table delegate's `replayInputs` is `delegate`: the table's objects are built in a frame of their own, so
  an object it holds is reported by the input that holds it. Rule: criterion 2 ("`replayInputs` says `delegate`").
- Decision: criterion 3 has no pair to rerun. P2-245's runs count no replay under "the model's runs call ...", and no
  SARIF of the 2026-10-19 runs on those checkouts holds the phrase (`docs/runs/2026-10-20-delegate-answers.md`), so a
  rerun would show nothing this ticket changes. Rule: the criterion's own scope.
- Note: the in-process `DriverTableTests` load the generated driver into a collectible `AssemblyLoadContext` and call
  its `Case` directly; `Main` writes the protocol to the process's standard output handle, which a test cannot capture.
