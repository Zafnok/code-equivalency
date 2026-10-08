# P2-144 A switch expression that matches no arm throws another exception type after a migration from .NET Framework, and such pairs are Equivalent by congruence
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A switch expression with no arm for the value throws. The compiler throws
`System.Runtime.CompilerServices.SwitchExpressionException` where the reference assemblies have the
type (.NET Core 3.0 and later) and `System.InvalidOperationException` where they do not (.NET
Framework). The source is the same on both sides, the two sides reach the throw on the same inputs,
and the exception type, which is an observable (VERIFICATION-MODEL section 1), differs.
`SwitchExpressionException` derives from `InvalidOperationException`, so a handler for the base
type sees no difference and a handler or a test for the exact type does.

P2-137 counted the callee pair `System.InvalidOperationException::.ctor()` to
`System.Runtime.CompilerServices.SwitchExpressionException::.ctor()` on `gitextensions-8522` (main
of 2026-10-07): 41 results name it in `properties.reboundCalls`. 39 of them are Equivalent with
`proofMethod` `congruence`, because the fingerprint of the bound tree does not hold the throw and
the body is not runtime-sensitive, and 2 are changed pairs, both with `switch-pattern` in the
reason set as well. So on an input that matches no arm, 39 results say Equivalent where the thrown
type differs. Whether any of the 39 can be reached with such an input was not checked: a switch
over every member of an enum still has the throw for a value outside the enum.

## Spec references
VERIFICATION-MODEL sections 1 and 3; ADR 0024 (congruence and runtime-sensitive bodies); ADR 0040
(a runtime rule applies only if the pair crosses it); ADR 0008 and
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`; ADR 0042;
`docs/tickets/done/P2-137-rebound-call-forms-counted-and-catalogued.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, over `gitextensions-8522` (`--lower-only`): the matched pairs whose bodies hold
   a switch expression with the compiler's throw, split by whether an arm always matches (a
   discard or `var` arm, or the patterns cover the type's every value), and by congruent or
   changed. Counts in `## Notes`.
2. Decide, through `equiv-adr`'s bar test, what the throw is on a pair that crosses from .NET
   Framework to .NET: a runtime rule (EQ006, as a `runtime-changes.json` row or its equal for a
   compiler-made call), so that such a body is runtime-sensitive and not congruent; or one
   exception type on both sides, recorded on the result as an assumption. Record the decision where
   the bar test says.
3. A sample pair, the same switch expression with no discard arm on .NET Framework 4.8 and on .NET
   10: its verdict is the one criterion 2 decides, and it is not Equivalent by congruence unless
   the decision says the two types are one.
4. A switch expression whose arms always match is unaffected: a sample method of it stays
   Equivalent by congruence.
5. On a re-run of `gitextensions-8522`, `## Notes` records the results by rule for the pairs of
   criterion 1, next to 39 Equivalent and 2 Unknown.

## Files
`src/Equiv.Frontend.CSharp/` (the fingerprint and the lowering of the switch expression's throw),
`src/Equiv.Core/RuntimeChanges/` if criterion 2 chooses a row, their tests, `samples/`, the ADR
criterion 2 names.

## Tests
A unit test of the fingerprint and of the lowering, and the sample of criteria 3 and 4.

## Size guard
A switch statement has no such throw and is out. Any change to when a call counts as rebound (ADR
0042's decision) is a different ticket.

## Out of scope
The `switch-pattern` opaque reason (P2-122).

## Notes
- Found by P2-137, criterion 1, whose out-of-scope list says to file it when the two sides reach
  the throw on the same inputs.
