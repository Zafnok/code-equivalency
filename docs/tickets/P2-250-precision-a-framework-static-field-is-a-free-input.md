# P2-250 Precision: a framework `static readonly` field the body reads is a free input, so `IntPtr.Zero` can differ between the sides
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-245's re-adjudication (`docs/runs/2026-10-19-execution-reach.md`) found three false EQ002 on `gitextensions-8522`
(`EasyHook.HookAccessControl::SetInclusiveACL(int[])`, `::SetExclusiveACL(int[])`, `EasyHook.LocalHook::get_HookBypassAddress()`,
P2-212 sample rows 0, 7, 18): both bodies are the same, and the solver's divergence rests on `field.System.IntPtr.Zero`,
a static field map keyed by the type's token that one side reads and the model gives a value other than zero. A
framework `static readonly` field (`IntPtr.Zero`, `String.Empty`, `SizeF.Empty`) has one value at run time, the same on
both sides, so a model that gives it another value, or two, is a state no run has. The same maps make replays
not constructible (`the model constrains field.System.IntPtr.Zero`: 4 replays, `field.System.String.Empty` 5, in P2-245's
runs). When done, a read of a framework `static readonly` field of a value or string type is no free input: either the
field's known value (where the IL or a catalogue gives it) or one shared, unwritten input both sides read, so that equal
reads are equal values.

## Spec references
VERIFICATION-MODEL section 2 (heap and nullness inputs, `field.*` maps keyed by the type's token), ADR 0018, ADR 0021
(shared inputs by name), `src/Equiv.Frontend.CSharp/Lowering/HeapLowerer.cs` (`Member`, the token of a static field),
`docs/runs/2026-10-19-execution-reach.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A pair whose two bodies read `IntPtr.Zero` (or `String.Empty`) the same way and differ in nothing else is not
   Divergent; a sample under `samples/` shows it.
2. Rerun `gitextensions-8522` in `full --execute` (compare mode quick, `--jobs 4`): the three results above are no longer
   EQ002, and each P2-245 replay reason naming a framework static field is gone or names something else; the run file
   says which.

## Notes
- Filed 2026-10-19 by P2-245 (finding from criterion 6's re-adjudication: Git Extensions rows 0, 7 and 18 are false
  positives by hand trace; a `probe` of 0 and 7 differed only because the native EasyHook DLL loads on one side's driver
  host, which P2-245 did not count as confirming).
