# P2-184 Precision bug: a call an API-equivalence entry rewrites is never closed, so it writes the heap on the legacy side only
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-130's adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 9 false EQ002 with one
cause, on all three migration pairs: 6 on `gitextensions-8522`, 2 on `openra-17989`, 1 on
`duplicati-3124`. Each body is unedited and calls `String.Split(params char[])` or
`String.TrimEnd(params char[])` with one character. `bcl.string-split-one-char` or
`bcl.string-trim-end-one-char` applies (`equivalencesApplied`), so both traces call the same modern
member with the same arguments. But `IrLowerer`'s rewritten call is built with `Closed: false`
(`IrLowerer.cs`, the `Rewritten` path), while the modern side's direct call of the same member gets
`ClosedCalls.IsClosed(target)`, which is true for these members (ADR 0041). So the legacy call
havocs every heap map the body touches and the modern call leaves the heap as it was; every later
call or field read then differs. P2-070 (done) added the entries and left this case.

When done, a rewritten call is closed exactly when a direct call of the modern member is.

## Spec references
ADR 0020, ADR 0041, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (`Rewritten`, `Callee`),
`ClosedCalls`, `docs/tickets/done/P2-070-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A call rewritten by a member entry takes its closedness from the modern member, as a direct call
   of that member does.
2. A sample pair (unedited body: `Split` with one character, then a field read and a call) is
   Equivalent; one whose two bodies differ after the call is not.
3. Rerun `gitextensions-8522` and `openra-17989` in compare mode quick; Notes list the 8 procedures of
   the verdict's appendix owned by this ticket on those pairs and their new results.

## Tests
`ApiEquivalenceLoweringTests` (the rewritten call is closed for a closed modern member, not for an open
one), and the sample pair as an integration test.

## Size guard
A change to which members are closed: stop, that is ADR 0041's list.

## Out of scope
The fingerprint text of a rewritten call (P2-177).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4). Procedures: the appendix rows owned by P2-184.
- Decision: closedness is read from the modern member's identity (`ClosedCalls.IsClosed(CallIdentity,
  Compilation)`), with each type it names resolved in the side's compilation, because the legacy side
  often does not declare the modern member (.NET Framework has no `String.Split(char,
  StringSplitOptions)`), so there is no symbol to ask. ADR 0041's rule is applied unchanged: the
  containing type, every parameter type and every type argument inert. A type that does not resolve
  on that side, a generic type and a generic containing type (`Nullable<T>`'s members) read as not
  inert, so such a call stays open: imprecise, never unsound. Every member entry's modern member in
  the catalogue reads the same as a direct call of it (five closed: `Split(char,StringSplitOptions)`,
  `Contains(char)`, `TrimEnd(char)`, `TrimEnd()`, `TrimStart()`; the span, `TimeSpan`, `Enum`,
  `FileSystemInfo` and Web API members open).
- Decision: patch release, no `Release:` footer, as for P2-188 to P2-190. No verdict's meaning
  changes; ADR 0056's taint now reaches the rewritten calls it was always meant to cover.
- Every call to the five closed members is now closed, so ADR 0056 taints them in the replay (it
  taints an identity every call to which is closed). Three sample Divergents that rested on what the
  solver chose for one of them are Unknown(Abstraction): `api-drift`'s `HasX` (the legacy rewritten
  `Contains` on a null source; filed P2-202), `bcl-overload-rebinding`'s `StripOther` and
  `bcl-rebound-overloads`' `TrimTailOther` (two different `TrimEnd` answers; filed P2-203). Each is a
  real difference that ADR 0056's Consequences already name as lost ("a divergence both sides reach
  only through a closed call's answer"); before this fix they were Divergent only because one side's
  call was open. Criterion 2's `Total` is Unknown(Abstraction) for the same reason: its difference
  shows only past `Split`, a closed call that can throw (P2-159 owns the throw list; `Split` with an
  arbitrary `StringSplitOptions` throws, so it stays off it).
- Criterion 3, 2026-10-15: `gitextensions-8522` and `openra-17989` in compare mode quick, `--jobs 4`,
  run on `main` at `0e18ba4c` (this branch's base) and on this branch, one after the other on the
  same box and checkouts. The verdict appendix's eight rows on these pairs:

  | pair | appendix row | procedure | before (P2-130) | now |
  |---|---|---|---|---|
  | gitextensions-8522 | 3 | `GitUI.CommandsDialogs.RevisionFileTreeController::SelectFileOrFolder(GitUI.UserControls.NativeTreeView,string)` | EQ002 | EQ001 (bounded) |
  | gitextensions-8522 | 7 | `GitUITests.Editor.Diff.LinePrefixHelperFixture::GetSegmentsForDiffText(string)` | EQ002 | EQ001 (lockstep-induction) |
  | gitextensions-8522 | 16 | `GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::IsCurrentlyOpenedWorktree(...WorkTree)` | EQ002 | EQ001 (bounded) |
  | gitextensions-8522 | 24 | `ICSharpCode.TextEditor.Document.FoldingManager::DeserializeFromString(string)` | EQ002 | EQ001 (lockstep-induction) |
  | gitextensions-8522 | 26 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::PopulateTreeView(GitUI.UserControls.NativeTreeView,string)` | EQ002 | EQ001 (lockstep-induction) |
  | gitextensions-8522 | 27 | `GitUI.FileStatusList::IsFilterMatch(GitCommands.GitItemStatus)` | EQ002 | EQ003 abstraction |
  | openra-17989 | 0 | `OpenRA.Mods.Common.Server.LobbyCommands::InterpretCommand(...,string)` | EQ002 | EQ001 (bounded) |
  | openra-17989 | 1 | `OpenRA.Mods.Common.LoadScreens.LogoStripeLoadScreen::Init(OpenRA.ModData,...)` | EQ002 | EQ001 (bounded) |

  `IsFilterMatch`: the heap is now the same on both sides past the rewritten `TrimEnd`; the model's
  first difference is past `System.IO.Path::GetFileName(string)`, a runtime-changed member whose two
  calls give two heaps. That is an unedited body a runtime rule keeps out of congruence: P2-181.
- Whole pairs, base to this branch (quick, `--jobs 4`): `gitextensions-8522` EQ001 12,858 to 12,873,
  EQ002 29 to 23, EQ003 582 to 575, EQ006 142 to 140; `openra-17989` EQ001 9,827 to 9,852, EQ002 2 to
  0, EQ003 254 to 231. Every result that moved has an entry applied. Up: 5 + 2 EQ002, 6 + 8
  Unknown(abstraction) and 4 + 15 Unknown(timeout) to EQ001 (the closed call leaves fewer heap maps
  to carry). Down, on Git Extensions only: `PathEqualityComparer::Equals(string,string)` and
  `::GetHashCode(string)` from EQ006 (the `GetFullPath` row) to Unknown(abstraction) naming
  `TrimEnd(char)`. The base witness for each did not show the row's change (the legacy side threw
  from `String.Compare`, not `ArgumentException` from `GetFullPath`); it rested on the legacy
  rewritten `TrimEnd`'s answers, open there only because of this bug, which is what ADR 0056 says
  no Divergent may rest on. Seven more (3 + 4) moved between Unknown reasons (abstraction, timeout or unaligned-loop to
  opaque or timeout).
