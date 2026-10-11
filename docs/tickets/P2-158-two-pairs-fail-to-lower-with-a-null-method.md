# P2-158 Two matched pairs of gitextensions-8522 fail to lower with a null `method`
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A `--lower-only` run of `gitextensions-8522` on `main` at `1f9bb699` exits 5. Two matched pairs fail to
lower, each with `Value cannot be null. (Parameter 'method')`:
- `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)`
- `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)`

The other 13,590 pairs lower. A construct the frontend cannot lower is an `IrOpaque`, never an exception
(`equiv-task-loop`), and a pair that fails to lower has no body for any census count. Which operation
hands a null method to which callee is not known: `ArgumentNullException.ThrowIfNull(method)` is in
`CallIdentityFactory`, `Forwarders`, `BodyFingerprinter` and `IrLowerer`. The first step is the repro.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md`; `docs/tickets/done/P2-083-binary-lowering-nullref-crash.md` (the same
kind of finding); `.claude/skills/equiv-corpus-run` (hard rule 4).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test in `tests/Equiv.Frontend.CSharp.Tests` lowers the smallest body that throws as the two
   constructors do, written from the operation that is the cause and not copied from the corpus.
2. That body lowers: to IR if the operation is one the lowering takes with a method it can name, otherwise
   to an opaque node with the operation's kind as the reason. Decide with `equiv-decide` and log it.
3. A `--lower-only` run of `gitextensions-8522` exits 0 with both pairs lowered, or `## Notes` records why not.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md` if a row changes.

## Tests
Named in criterion 1.

## Size guard
More than one cause fixed means the ticket was misread: file the second.

## Out of scope
Lowering anything the frontend leaves opaque today.

## Notes
- Found by P2-141 (2026-10-11), whose two census runs of `gitextensions-8522` both hit it, with and without that
  ticket's change.
