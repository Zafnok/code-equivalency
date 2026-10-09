# P2-149 Soundness: a type's layout can change what an ordinary body does, and no fingerprint of such a body holds it
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-146

## Goal
P2-146 put the declarations of the types in an imported function's signature into that function's
text, because they say how its arguments are marshalled. It left a type's declaration out of every
other text (ADR 0024, clarification of 2026-10-09, "Why a type's lines belong to the function").
A body with no `extern` function can also depend on how a type is laid out, and the bound tree of
such a body names the type and its members and nothing of their attributes. A pair that differs only
in one of the following has equal fingerprints and is Equivalent by congruence (ADR 0024 decision 1),
on a same-runtime pair as well:

- `[FieldOffset]` on a field of a `[StructLayout(LayoutKind.Explicit)]` type the body reads or
  writes. Two fields at one offset are one storage location; at different offsets they are two;
- `[StructLayout]` and its `Pack` and `Size`, and the order of a type's fields, where the body takes
  `sizeof` of the type, calls `Marshal.SizeOf` or `Unsafe.SizeOf`, or reads it through a pointer or a
  span of bytes;
- `[InlineArray(n)]`, whose length is the attribute's argument;
- `[UnmanagedFunctionPointer]` on a delegate type the body passes to
  `Marshal.GetFunctionPointerForDelegate`, and the assembly's `[DisableRuntimeMarshalling]` where
  the body calls through a `delegate* unmanaged`.

Found while working P2-146 by reading what `BoundSerialiser` writes for a field reference, a
`sizeof` and a function-pointer call; none was reproduced as a wrong result. The first step is the
repro.

## Spec references
ADR 0024 (`docs/adr/0024-congruence-by-bound-fingerprint.md`, decision 1 and the clarification of
2026-10-09), ADR 0019 (a verdict assumes its callees, and a member's change is caught by its own
pair; a type has no pair), `docs/tickets/done/P2-146-soundness-interop-settings-outside-the-function.md`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Symbols`, `Member`, `Marshalled`,
`Declared`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Each bullet of the Goal is a unit test on `BodyFingerprinter` that either shows two different
   fingerprints today, in which case the bullet is struck from this ticket with the test kept, or
   fails before the fix.
2. Before any change to `BoundSerialiser`: decide through `.claude/skills/equiv-adr`'s bar test what
   a body's text holds of a type it uses, and record which row applied in `## Notes`. The decision
   says, at least: which operations make a body depend on a type's layout (every use of the type,
   or the ones the Goal lists); whether the solver path needs the same fact, since a pair whose
   fingerprints differ is then lowered and the IR has no layout either; and what a type from a
   reference is taken to be.
3. The decision of criterion 2 is implemented. A body that uses no type with a layout attribute
   keeps the text it has; a test asserts this.
4. The public-corpus results that change are counted in `## Notes`, by rule id before and after,
   from one run of `powershell-19687` in compare mode quick on each side of the change.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, its tests, and the ADR or
clarification criterion 2 produces.

## Tests
In `BodyFingerprinterTests`: one test per bullet of the Goal, and
`ABodyThatUsesNoLaidOutTypeKeepsItsText`.

## Size guard
One pull request after the decision. If criterion 2 decides that the lowering must change as well,
stop after the fingerprint and file the rest.

## Out of scope
- An imported function's text (P2-146).
- A type from a reference.
- Attributes that change no layout and no marshalling.

## Notes
- Filed 2026-10-09 from P2-146.
