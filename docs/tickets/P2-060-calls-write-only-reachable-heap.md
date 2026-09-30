# P2-060 A call writes only the heap its callee can reach, so a BCL call cannot change a user field
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-017

## Goal
ADR 0018 makes every call read and write every `field.*` and `array.*` map the body touches. That is
sound, but it lets the solver build a Divergent on a heap no real callee produces. P1-017's IL mode
of the differential gate found one: `z = ($"{s}t" == null);`, lowered from IL as
`String.Concat(string, string)`, is followed by a loop that reads `F` and `u[0]`. Z3 chose a
`Concat` that writes both, the loop then ran on only one side (a dropped null check), and the
Divergent's model returns `False` on both sides in C#. The same pair written `s + "t"` gives the
same Divergent from IOperation. `String.Concat` cannot name `Oracle`, so it cannot write
`Oracle.F`. Every such result on real code is a false EQ002, and P2-047's audit has 16 refuted
Divergents on Git Extensions whose cause is not yet known. Decide, through `equiv-adr`, which heap
maps a call may write when its callee's assembly cannot reach the map's type. Then implement that.

## Spec references
ADR 0018 (calls read and write the heap), ADR 0026 and its 2026-09-30 clarification, ADR 0019,
VERIFICATION-MODEL section 7 (rule 2).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome (a clarification on ADR 0018 or a new ADR) is merged before any code
   change. It covers callbacks: a delegate, virtual or interface argument, or `object` receiver,
   through which a BCL callee can reach user code.
2. A call whose callee cannot reach a map's declaring type, directly or through such an argument,
   leaves that map unchanged in both lowerings.
3. The pair above is pinned as a test in `DifferentialSoundnessTests`. Its verdict is Unknown or
   Equivalent, never a Divergent that does not replay.
4. Under the IL mode of the gate, rules 1 to 3 hold on the PR and nightly budgets. The rule 2
   exemption for models through a call (the ADR 0026 clarification) is narrowed only as far as that
   allows; see the Deviation in Notes.

## Tests
- `DifferentialSoundnessTests.ABclCallCannotWriteAUserField`
- the unit tests the decision names, in `Equiv.Frontend.CSharp.Tests`

## Out of scope
Tainting ordinary call results (ADR 0026 rejects it). Callee summaries beyond reachability.

## Notes
- Decision (criterion 1, through `equiv-adr`): a new ADR, 0041 (PR #309), not a clarification. It
  narrows ADR 0018's Decision and changes a trace event's shape. A call is closed when its callee's
  containing type, parameter types and type arguments are all inert: `bool`, `char`, the 8- to 64-bit
  integers, `float`, `double`, `decimal`, `string`, enums, and `Nullable<T>` of those. A closed call
  reads and writes no heap map, and its event has no heap. Every other call keeps ADR 0018's rule.
  Per-map reachability by assembly references was rejected: `thread.Join()` is handed nothing, yet it
  runs a delegate stored earlier.
- Decision: `IrCall.Closed` (IR text `call closed "..."`, validator IR013 rejects a closed call with
  heap pairs). The frontend decides it in one place (`ClosedCalls.IsClosed`), and `SsaBuilder` gives a
  closed call no heap pairs. The encoder treats an identity as closed only if every call to it in the
  product is closed. An API-equivalence adapter's call names its modern member only by string, so it
  stays open, and a direct call to the same member then shares the open encoding. `ICallOracle` is
  unchanged: the model oracle asks `TraceEncoder.IsClosed`.
- Decision (criterion 3): the gate's original pair (seed `000000000000` in P1-017) cannot be
  recovered, because CsCheck's parallel sampling reports a different first failure from run to run.
  `ABclCallCannotWriteAUserField` pins the shape the ticket names. It reads `F` before or after
  `$"{s}t" == null` (from IL) or `s + "t" == null` (from IOperation), then runs a loop that reads
  `u[0]`. The pair is Divergent before this change and Equivalent after it, under both lowerings.
- Deviation (criterion 4, measured): rule 2's exemption cannot be narrowed to open calls.
  Narrowed, the gate passes the PR budget (200 pairs). It fails the nightly budget: 5,000 pairs,
  seed `9v8KtBVEhjFh`, DropNullCheck under IL, where the model has
  `Nullable<int>.GetValueOrDefault()` throw before a statement the other side runs first. That
  channel is a closed call's `threw` flag or result, not the heap. Two samples of IL pairs holding
  `$"..."` or `int?` were taken with rule 2's exemption switched off. They gave 12 non-replaying
  Divergents before this change and 7 after (CsCheck draws differently each run). Replayed after
  this change with the exemption narrowed to open calls, 6 of the 19 still fail rule 2. In 5 of
  them the model made a `Nullable` member throw `System.Exception`. So the exemption stays for any
  call. ADR 0041 and a second ADR
  0026 clarification say why, and criterion 4's text is corrected. Filed P2-068 for that channel.
