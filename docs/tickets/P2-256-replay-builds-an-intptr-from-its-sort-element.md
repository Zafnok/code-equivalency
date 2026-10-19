# P2-256 Execution reach: a replay builds an `IntPtr` field or argument, which the model holds as a sort element
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-250

## Goal
The C# lowering maps `IntPtr` and `UIntPtr` to the sort of their metadata name, and since P2-250 reads `IntPtr.Zero` as
element 0 of that sort. Replay builds no value of that sort, so a replay whose model holds an `IntPtr` field or argument
is `not-constructible` ("no `System.IntPtr` argument can be built for this's `HookAccessControl.m_Handle` from the
model"): two EQ002 replays on `gitextensions-8522` in P2-250's run (`EasyHook.HookAccessControl::SetInclusiveACL(int[])`
and `::SetExclusiveACL(int[])`), both real behaviour changes by hand trace. When done, element 0 is `IntPtr.Zero` and any
other element a distinct non-zero value, so equal elements are equal values and the model's tests against `IntPtr.Zero`
hold. A non-zero value is not a handle any native code owns, so a driver that would pass one to a P/Invoke is a case
for ADR 0035's safety rule, not a replay that runs.

## Spec references
VERIFICATION-MODEL section 2 (P2-250's paragraph on frozen fields), section 6 (the replay paragraph), ADR 0062
decision 3, `src/Equiv.Frontend.CSharp/Execution/ModelObjects.cs`, `ReplayArguments.Wire`, P2-251 (the enum case).

## Acceptance criteria (all must hold; nothing beyond them)
1. A replay builds an `IntPtr` or `UIntPtr` argument or field from its sort element as the Goal says; a unit test
   covers element 0 and two other elements.
2. The two replays above are rerun and each one's replay outcome, or the reason it does not run, is given.

## Notes
- Filed 2026-10-19 by P2-250 (a `not-constructible` reason that P2-250's removal of `field.System.IntPtr.Zero` from the
  models uncovered).
