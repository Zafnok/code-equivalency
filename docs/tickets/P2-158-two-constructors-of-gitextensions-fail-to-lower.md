# P2-158 Two constructors of gitextensions-8522 fail to lower with a null method
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A census (`--lower-only`) of `gitextensions-8522` (legacy `3f4ed21998af`, modern `5190ba5c1a5f`) at
equiv `1f9bb699` exits 5: two matched pairs fail to lower, the same way on both sides and in every
run of 2026-10-11:

- `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)`
- `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)`

each with `Value cannot be null. (Parameter 'method')`, an `ArgumentNullException` from a guard in
the C# frontend's lowering (`CallIdentityFactory.Name`, `IrLowerer.Lower` and `Forwarders` each have
one for a parameter of that name). The run goes on (ADR 0023) and the two pairs are counted as
unlowered. P2-139's census of `gitextensions-9860` at `feee17e1` exited 0, so a commit after that one
is the likely cause: P2-154 (#468), P2-139 (#469), P2-103 (#470) or P1-020 (#471).

When this is done the two bodies lower, to IR or to an opaque with a reason, and the census exits 0.

## Spec references
ADR 0023 (a crashing pair does not end the run); CLAUDE.md "Unsupported constructs in the frontend
become `IrOpaque`, never `throw`"; `.claude/skills/equiv-corpus-run` (how to fetch and run the pair).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test in `tests/Equiv.Frontend.CSharp.Tests` lowers the smallest snippet that reproduces
   the exception, written from the construct and not copied from the corpus, and fails before the fix.
2. The construct lowers as VERIFICATION-MODEL.md says it should, or to `IrOpaque` with a reason the
   coverage table names; no guard is removed to get there.
3. `## Notes` names the commit that introduced the failure and the operation that reached the guard
   with a null method.
4. A census of `gitextensions-8522` exits 0, or `## Notes` lists what else still fails.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md` if a row changes.

## Tests
Named in criterion 1.

## Size guard
If the null method comes from more than one construct, fix the one these two bodies reach and file
the others.

## Out of scope
The IL fallback. Any change to what the two bodies' other constructs lower to.

## Notes
- Found by P2-140's census of 2026-10-11. Both bodies read attributes of an XML element inside
  `try` blocks, parse them with `bool.Parse`, and make a reflection call that is handed
  `binder: null` and a new empty `object[]`; which of those reaches the guard is criterion 3.
- Cause (criterion 3): `4685d823` (P2-074, #464) introduced it, not one of the four commits the Goal suspects.
  `ValidPaths.IsValid` took `property.Property.GetMethod!` of every `IPropertyReferenceOperation` it followed, and a
  property that overrides only the setter (`XmlAttribute.InnerText`) has no getter of its own. Both bodies assign such
  a read to a local and call `string.StartsWith` on it; that call has a runtime-change row, the receiver is no
  constant so the row flags it, the flagged call is matched once more through `ValidPaths`, which follows the local to
  its initializer and hands `CallIdentityFactory.Name` the null getter. The `bool.Parse` calls and the reflection
  call have no row and never get there.
- Decision: such a read is not a valid path (the row stays flagged), which is what any read that names no listed
  source already is. The overridden property's getter is not looked up: `IrLowerer` leaves the read itself opaque
  with reason `PropertyReference`, and this ticket's Out of scope keeps that as it is. No coverage-table row changes.
- Census (criterion 4): `corpus.ps1 -Compare gitextensions-8522 -LowerOnly` at `1fe6b57f` exits 0 in 129 s, with no
  `Value cannot be null` in the console or the progress log. No `docs/runs/` summary: the run checks one thing.
- Not looked into: why P2-139's census of `gitextensions-9860` at `feee17e1`, which already had `4685d823`, exited 0.
