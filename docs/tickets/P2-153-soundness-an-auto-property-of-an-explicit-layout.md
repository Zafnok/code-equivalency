# P2-153 Soundness: the text of a body that uses an auto-property or an event of an explicit layout holds none of the layout
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-150

## Goal
P2-149 writes a type's declaration after a reference to a field that has a `[FieldOffset]` (ADR
0024, clarification of 2026-10-09 (P2-149)). An auto-property and a field-like event of an explicit
layout are storage at an offset too, through `[field: FieldOffset(n)]`:

    [StructLayout(LayoutKind.Explicit)]
    class U { [field: FieldOffset(0)] public int A { get; set; } [field: FieldOffset(0)] public int B { get; set; } }
    int M(U u) { u.A = 1; return u.B; }

`M`'s text names the properties `A` and `B`. The bound tree does not name their backing fields, so
the text has no `layout` line, and a pair that changes only an offset has equal fingerprints for
`M` and is Equivalent by congruence, although `M` returns another number. ADR 0019 does not cover
it: the accessors have no body, so their own pairs are unchanged, and a type has no pair.

P2-150 closed the solver path: the lowering makes such a reference an opaque that nothing shares
(`Layouts.IsOverlaid`), because no fingerprint holds the declaration. Once the text holds it, that
opaque can be shared by its fingerprint as a field's is.

Found while working P2-150 by reading `Layouts.Reached`; not reproduced. The first step is the
repro.

## Spec references
ADR 0024 (the clarifications of 2026-10-09 (P2-149) and (P2-150)), ADR 0019,
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs` (`Reached`, `IsOverlaid`),
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (`Layout`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test on `BodyFingerprinter` shows `M` above with one fingerprint beside two declarations
   of `U` that differ in an offset, and the same for a field-like event read in its own type, or
   the Goal is struck with the tests kept.
2. `Layouts.Reached` yields the declaring type for a reference to a property or an event that
   `Layouts.IsOverlaid` holds, so the text has the declaration after it. A dated clarification
   under ADR 0024 says so. A body that uses no such member keeps its text; a test asserts it.
3. `IrLowerer.Layout` shares the opaque of such a reference by its fingerprint, as a field's. The
   rows of `docs/tickets/IOPERATION-COVERAGE.md` that say "shared: no" for it say why it is now
   shared, and `AnAutoPropertyOrAnEventOfAnExplicitLayoutIsAnOpaqueNothingShares` is replaced by a
   test that the fingerprint differs beside another declaration.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Layouts.cs`, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`,
their tests, `docs/adr/0024-congruence-by-bound-fingerprint.md`,
`docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
In `BodyFingerprinterTests`: `AnAutoPropertysFieldOffsetIsInTheTextOfABodyThatUsesIt`. In
`IrLowererTests`: the replacement criterion 3 names.

## Size guard
One rule, two members. If the repro shows another member whose storage the bound tree does not
name, stop and file it.

## Out of scope
- A type from a reference.
- A model of the shared storage (ADR 0024, clarification of 2026-10-09 (P2-150), Rejected).

## Notes
- Filed 2026-10-09 from P2-150.
