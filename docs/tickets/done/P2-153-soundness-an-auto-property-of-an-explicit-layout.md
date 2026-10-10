# P2-153 Soundness: the text of a body that uses an auto-property or an event of an explicit layout holds none of the layout
Status: done (PR #453)
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
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (a type's declaration holds its
events), their tests, `tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`, `docs/adr/0024-congruence-by-bound-fingerprint.md`,
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

### Criterion 1: the repro
- Reproduced on the P2-150 branch at `f43dbfb2`. `AnAutoPropertysFieldOffsetIsInTheTextOfABodyThatUsesIt`
  failed all five of its cases with "Values are equal": a write and a read, a read alone and an
  increment of an auto-property, and `+=` and `-=` on a field-like event, each beside two
  declarations that differ in one offset. Nothing is struck.
- Deviation: criterion 1 says "a field-like event read in its own type". The fingerprint tests'
  method is in `N.C`, so the event cases subscribe from outside (`u.E += h`), which references the
  event the same way. The read inside the type is tested at the lowering
  (`AnAutoPropertyOrAnEventOfAnExplicitLayoutIsAnOpaqueWhoseFingerprintHoldsTheDeclaration`).

### Criteria 2 and 3
- `equiv-adr` bar test, row 1: ADR 0024 decides, and this applies it to two members its
  clarification of P2-149 did not spell out. Vehicle: the dated clarification 2026-10-09 (P2-153).
- Found by the repro, and the size guard's case: a field-like event's backing field is no symbol
  of its type. `GetMembers()` does not return it, so (a) `Layouts.IsOverlaid` could not ask it for
  a `[FieldOffset]`, and its P2-150 stand-in, "the type has a field with one", is false for a type
  whose only storage is events and auto-properties; and (b) a type's declaration in the text held
  none of its events, so moving an event's offset changed no text. Both are the event half of
  this ticket's one rule, so they are fixed here and not filed.
- Decision: how an event is known to be at an offset -> its type has `[StructLayout(LayoutKind.Explicit)]`. Alternatives: the type has a field with a `[FieldOffset]` (false for a type of events alone), the event's own `[field: FieldOffset]` read from syntax at every reference (a syntax read per lowered operation, for what the compiler already guarantees: CS0625 requires the attribute on every instance field of an explicit layout). Rule: 4.
- Decision: how the backing field's attributes reach the text -> from the event's declaration, each attribute bound with `GetOperation` in the compilation that declares it and written as its constructor, named arguments and constants. Alternatives: the attribute's source text (a constant named in it could change elsewhere and leave the text the same, which is a false congruence), nothing (the repro). Rule: 1.
- Decision: which events a declaration holds -> every field-like instance event, with its type, not only those of an explicit layout. Alternatives: only where the type is explicit (an event's backing field is an instance field of a sequential layout too, and the declaration already holds every other one). Rule: 4. Cost: a text that held such a type's declaration gains the event's lines and reaches its delegate type, the same on both sides.
- Decision: the `[StructLayout((short)2)]` constructor -> not read. The compiler does not take it for an explicit layout either: a `[FieldOffset]` under it is CS0636. Rule: 5.
- `IrLowerer.Layout` no longer asks what kind of member the storage is: every `Layout` opaque is
  shared by its fragment's fingerprint.
- Added beyond the ticket's Tests: `LayoutEquivalenceTests.TheSameUseOfAnExplicitLayoutIsSharedBesideOneDeclarationOnly`
  (Z3): for fields and for auto-properties, the same write and read in the same order is
  Equivalent beside one declaration in a pair that differs elsewhere, and not beside two.
- Not run: the corpus. No criterion asks for it; P2-150's run found no explicit layout in a
  changed pair of `powershell-19687`.

