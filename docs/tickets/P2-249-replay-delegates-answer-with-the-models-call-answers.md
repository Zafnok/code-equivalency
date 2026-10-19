# P2-249 Execution reach: a replay builds a delegate argument the model's runs call, answering each call as the model does
Status: todo
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
