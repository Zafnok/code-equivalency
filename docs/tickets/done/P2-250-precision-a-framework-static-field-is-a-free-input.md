# P2-250 Precision: a framework `static readonly` field the body reads is a free input, so `IntPtr.Zero` can differ between the sides
Status: done (PR #554)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-245's re-adjudication (`docs/runs/2026-10-19-execution-reach.md`) found four false EQ002 on `gitextensions-8522`
(`EasyHook.HookAccessControl::SetInclusiveACL(int[])`, `::SetExclusiveACL(int[])`, `EasyHook.LocalHook::get_HookBypassAddress()`
and `::IsThreadIntercepted(int)`, P2-212 sample rows 0, 7, 18 and 3): both bodies are the same, and the solver's divergence rests on `field.System.IntPtr.Zero`,
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
2. Rerun `gitextensions-8522` in `full --execute` (compare mode quick, `--jobs 4`): the run file says, for each of the
   four results above, its rule and why, and each P2-245 replay reason naming a framework static field is gone or names
   something else; the run file says which. (Corrected by this ticket: the four are real behaviour changes, see the
   `Deviation:` note, so they stay EQ002.)

## Notes
- Filed 2026-10-19 by P2-245 (finding from criterion 6's re-adjudication: Git Extensions rows 0, 3, 7 and 18 are false
  positives by hand trace; a `probe` of 0 and 7 differed only because the native EasyHook DLL loads on one side's driver
  host, which P2-245 did not count as confirming).
- Deviation: criterion 2 asked that the four Git Extensions results stop being EQ002. They are real behaviour changes,
  not false positives: the modern side of `EasyHook.HookAccessControl` and `EasyHook.LocalHook` is not the
  `Externals/EasyHook` project (no longer in the modern solution) but GitUI's copy, `GitUI/Theming/LocalHook.cs`, which
  wraps every native call (`LhSetGlobalInclusiveACL`, `LhSetInclusiveACL`, `LhSetExclusiveACL`,
  `LhGetHookBypassAddress`, `LhIsThreadIntercepted`) in `#if SUPPORT_THEMES`, a symbol no project, props or targets
  file of the modern checkout defines. So legacy makes the P/Invoke and modern does not: `SetInclusiveACL` and
  `SetExclusiveACL` change no native ACL, `HookBypassAddress` returns `IntPtr.Zero` and `IsThreadIntercepted` returns
  `false`. P2-245's `probe` saw exactly this (legacy threw `DllNotFoundException`, modern returned). Making them
  anything but Divergent would be a false Equivalent, so the criterion is corrected to report each one's rule and
  reason, and the run file re-adjudicates the four as confirmed. Criterion 1 and the Goal's model change stand: they
  are what removes `IntPtr.Zero` and `String.Empty` as free, call-written inputs.
- Decision: a frozen field keeps its `field.<Type>.<Field>` map, made `In` and left out of every call's heap pairs,
  rather than a new input family: the encoder goes by the parameter's kind, and the model decoder, replay and naming
  rule need no change.
- Decision: "framework" is "declared in metadata" (no declaring syntax). What makes the field unwritable is
  `static readonly` plus not being in the pair: a source field's static constructor is a body of the pair and writes
  it, while one in a referenced assembly is written only by a constructor the pair never lowers. Fields of a reference
  type stay `Ref`, as the Goal limits this to value and string types.
- Decision: the catalogue is `String.Empty` (the constant `""`) and fields whose value is their type's `default`
  (`IntPtr.Zero`, `UIntPtr.Zero`, `Guid.Empty`, `TimeSpan.Zero`, `DateTime.MinValue`, the `System.Drawing` `Empty`
  structs), read as element 0 of the sort as P2-138 reads an omitted struct argument. Each of those types' instance
  fields is private, so no body writes element 0's fields. The IL lowering reads them the same way, so a rebound body
  and a source body agree.
- The sample's four Equivalent pairs are each Divergent with `FrameworkStatics` switched off (checked locally by
  making `IsFrozen` false and running the sample's end-to-end test: four EQ002).
- Criterion 1: `samples/framework-static-fields` (`Describe`, `Trimmed`, `Release`, `Forever` Equivalent; `Blank`
  Divergent), asserted by `SamplesEndToEndTests.FrameworkStaticFields_AFrameworkStaticFieldIsOneValueOnBothSides`.
- Criterion 2: `docs/runs/2026-10-19-framework-static-fields.md`. The four stay EQ002 (confirmed); the nine replay
  reasons naming a framework static field are gone, each now naming its next obstacle; two Unknowns became confirmed
  EQ002. A baseline run with the change switched off separates this ticket from P2-253's effect on this host, which
  has no `Microsoft.WindowsDesktop.App` 5.0. Filed P2-256 (replay builds no `IntPtr`).
