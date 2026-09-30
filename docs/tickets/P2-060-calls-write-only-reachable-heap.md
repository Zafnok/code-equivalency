# P2-060 A call writes only the heap its callee can reach, so a BCL call cannot change a user field
Status: todo
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
4. Under the IL mode of the gate, the rule 2 exemption for models through a call (the ADR 0026
   clarification) is narrowed to calls that can still reach the heap. Rules 1 and 3 still hold on
   the PR and nightly budgets.

## Tests
- `DifferentialSoundnessTests.ABclCallCannotWriteAUserField`
- the unit tests the decision names, in `Equiv.Frontend.CSharp.Tests`

## Out of scope
Tainting ordinary call results (ADR 0026 rejects it). Callee summaries beyond reachability.

## Notes
