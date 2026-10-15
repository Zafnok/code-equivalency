# P2-216 Precision: five more runtime rows fire on calls their change cannot reach
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-073, P2-075, P2-189 and P2-190 (done) gave rows a precondition or narrowed them to the calls their change
affects. P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`) found 5 false EQ006 in its fresh
samples on rows they did not cover, each a call whose arguments or receiver cannot reach the row's change:
- `System.Diagnostics.Process::get_StartInfo(` on a `Process` the method itself creates with `new Process { ... }`:
  only a process this code did not start (`GetProcesses`, `GetProcessById`) reaches the change; a new one gets a
  fresh `ProcessStartInfo` on both runtimes (`gitextensions-8522`, `ResourceManager.LinkFactory::ExecuteLink(...)`);
- the UTF-8 decoding rows (`utf8Decoding`) on a local `byte[]` the method fills element by element with constants
  that form well-formed UTF-8: only ill-formed input decodes differently (`gitextensions-8522`,
  `GitCommandsTests.Git.EncodingHelperTest::DecodeStringTestUtf8()`);
- `System.Windows.Forms.TreeNode::set_ImageIndex(` and `set_SelectedImageIndex(`, with no precondition, on
  `ImageList.Images.Count - 1`, which is never below -1: only a value below -1 reaches the out-of-range change
  (`gitextensions-8522`, `SettingsTreeViewUserControl::AddPage(...)`);
- the `Environment.OSVersion` row on `.Version.Major` compared with the constant 6, a threshold the compatibility
  shim never crosses: P2-190's `shimmedVersionRead` reads `.Version` compared with a `Version` and not `.Major`
  compared with an `int` (`gitextensions-8522`, `GitUI.UserControls.ExListView::.ctor()`);
- the ICU `System.String::Equals(string,System.StringComparison)` row with `ordinalUnaffected`, where the comparison
  is a solution getter whose every return is `Ordinal` or `OrdinalIgnoreCase`: the exemption reads a constant only
  (`duplicati-3124`, `Duplicati.Library.Snapshots.UsnJournalService::IsPathEnumerated(string)`).

When done, each of these rows matches only calls that can reach its change.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (`precondition`, `shimmedVersionRead`, `ordinalUnaffected`),
`docs/tickets/done/P2-190-*.md` and `P2-189-*.md` (how a row is narrowed and its soundness argued), ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the five shapes gets a precondition or a narrowing, each with its soundness argument in Notes (what
   every call it exempts cannot reach), or `equiv-decide` records why one is not taken.
2. A sample pair per shape taken, EQ006 before and Equivalent after, and one where the change is reachable that
   stays EQ006.
3. `gitextensions-8522` and `duplicati-3124` rerun in compare mode quick; Notes give the five procedures' new
   results and every other result that moved.

## Tests
The runtime-change table tests; the samples.

## Size guard
Data flow beyond one method for any shape: take the shapes that need none and file the rest.

## Out of scope
Path rows (P2-207, P2-208, P2-217, P2-218).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6). Procedures and fingerprints are in the verdict's appendix.
