# P2-173 On a same-runtime pair, a result says why a body in a byte-identical file is not congruent
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-107

## Goal
On a pair whose two sides target the same runtime, a body in a file that is byte-identical on both
sides should be congruent unless something outside the file changed what it means: a type it names
was edited, a callee it binds to moved, a generated constant differs. P2-110's quick runs at equiv
`78853202` (`docs/runs/2026-10-13-cleanup-verdict.md`) find 11 such bodies that are not congruent
and are neither `unbound`, nor `no-body`, nor the known commit constant. Nothing in the SARIF says
why, so the cause of none of them is known.

On `powershell-19687`, 8. The pull request edits 9 files, all in
`Microsoft.Management.Infrastructure.CimCmdlets`; these are in two other assemblies:
- proved by the solver: `System.Management.Automation.CompletionCompleters::CompleteRequires(System.Management.Automation.CompletionContext,ref int,ref int)`,
  `System.Management.Automation.CommandSearcher::setupPathSearcher()`,
  `Microsoft.PowerShell.ConsoleHostUserInterface::WriteChoiceHelper(string,System.ConsoleColor,System.ConsoleColor,ref int,int)`
- Unknown (`opaque`): `Microsoft.PowerShell.Commands.GetExperimentalFeatureCommand::GetValidModuleFiles(System.Collections.Generic.HashSet<string>)`,
  `Microsoft.PowerShell.Commands.ModuleCmdletBase::GetModuleForNames(System.Collections.Generic.List<string>,bool,bool)`,
  `System.Management.Automation.SessionStateInternal::NewItem(string[],string,string,object,System.Management.Automation.CmdletProviderContext)`
- Unknown (`timeout`): `System.Management.Automation.NativeCommandProcessor::Complete()`,
  `Microsoft.PowerShell.Commands.NewPSDriveCommand::ProcessRecord()`

On `gitextensions-11372`, 2: `GitCommands.PathUtil::GetRepositoryName(string)` (proved) and
`GitExtensions.Program::Main()` (Unknown, `abstraction`). On `gitextensions-11284`, 1:
`GitUI.UserControls.RevisionGrid.CopyContextMenuItem::AddItem(string,string,System.Drawing.Image,char?)`
(proved).

Five of the 11 are proved, so for those the cost is solver time. Six are Unknown results a
reviewer is handed for code nobody touched. One possible cause on PowerShell is the 17 `unbound`
bodies of the same run (P2-172): a body that calls one of them may not be allowed to be congruent.
That is a guess.

When done, a result that is not congruent on a pair with no runtime change carries what kept it
out of congruence, and the 11 are explained: each is either right to be out (and the reason is
named), or a precision bug with its own ticket or fix.

## Spec references
ADR 0018 (congruence) and ADR 0024 (what a fingerprint holds); ADR 0034 (a changed pair);
ADR 0046 (shared inputs and generated constants); the soundness tickets that added to the
fingerprint since 2026-10-02 (P2-127, P2-145, P2-157, P2-161, P2-162) and their Notes;
`docs/tickets/done/P2-107-*.md`; `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A result whose pair is not congruent names the first fingerprint component on which the two
   sides differ (for example: body IR, a named type's layout, a callee's identity, a shared-input
   constant, an opaque that is never shared), by component kind and the identity it concerns, never
   by source text or value. The property is documented in `docs/VERIFICATION-MODEL.md`, with the
   other result properties. This is a
   public surface: add the `Release:` footer `equiv-release` says.
2. Rerun the three cleanup pairs in compare mode quick and record, for each of the 11 procedures
   above, the component named. Table in Notes.
3. Each of the 11 is classed in Notes as "right to be out of congruence" (with the edit of the pull
   request, or the generated difference, that reaches it) or as a precision bug. Each precision bug
   whose fix is not a few lines is a new `P2-nnn` ticket; a soundness doubt found on the way is a
   `P2-nnn-soundness-` ticket and is shown to the user first.
4. `corpus.ps1 -Metrics` prints the count of non-congruent pairs by component kind.

## Tests
- A unit test per component kind the property can name.
- A snapshot test (Verify) of a SARIF result carrying the property.

## Size guard
Fixing a precision bug criterion 3 finds, beyond a few lines: stop, that is the new ticket.

## Out of scope
Pairs that cross a runtime, where a runtime rule is the usual reason and is already named.

## Notes
- Filed 2026-10-13 from P2-110's rerun of the cleanup pairs. No ticket owned these.
- The same question came up on the upgrades (`docs/runs/2026-10-12-upgrade-verdict.md`, "Why
  unedited code is not congruent"), where it was answered by a text scan of the bodies. The
  property of criterion 1 replaces the scan.
- Decision: the property is `properties.notCongruent`, `{ component, identity }`, with fourteen
  component kinds (`docs/VERIFICATION-MODEL.md` section 6 has the table). The serialiser's text is
  unchanged: it can also be written as lines of fields, each tagged with its component, and the two
  sides' lines are compared only for a pair whose hashes differ, so a congruent pair costs nothing.
- Decision: "first" is the first line of the two texts that differs, and within an operation's
  line the first of kind, type, constant, symbols, context. Where one side has a declaration's line
  (a type's layout, an attribute, an import) and the other an operation's or none, the declaration
  is named: a type that gained a field has one more line, and the operation the other side has
  there did not change.
- Decision: a constant is named by the member the operation references (a `const` field) or by the
  operation's kind, never by its value; a caller location is part of the same field. Nothing in the
  serialiser can make a caller location the first difference by itself, so it has no kind of its own.
- Decision: equal texts kept out by a body that does not bind are `unbound`, set by the CLI, which
  is where that is decided (ADR 0029 decision 2); texts that differ are named by their difference
  first. Equal texts that are runtime-sensitive are `runtime-rule` with the side, and no more: the
  ticket leaves pairs that cross a runtime out.
- Decision: no ADR. The property reports what ADR 0024's text already holds and changes no verdict
  (`equiv-adr` bar test: a SARIF property beside `reboundCalls` and `forwardersResolved`).
- The runs (criterion 2), 2026-10-13, equiv `269c7d60`, compare mode quick, no `--jobs` (24
  processors), one after the other, `.corpus/` made new: `gitextensions-11372` 303 s exit 1,
  `gitextensions-11284` 267 s exit 1, `powershell-19687` 177 s exit 0. A first PowerShell run
  exited 4 in 16 s because the restore had not been given `UseRidGraph` (`tools/corpus/README.md`
  has the three steps); it is void and was run again.
- Results that carry the property, by component (`corpus.ps1 -Metrics`, criterion 4):
  `gitextensions-11372` operation 309, no-fingerprint 6, constant 2, callee 2;
  `gitextensions-11284` operation 169, no-fingerprint 5, constant 2, callee 2;
  `powershell-19687` unbound 17, operation 12, callee 8.
- The 11 (criteria 2 and 3):

  | Pair | Procedure | Result | `component` | `identity` | Class |
  |---|---|---|---|---|---|
  | powershell-19687 | `CompletionCompleters::CompleteRequires(...)` | Equivalent, lockstep-induction | callee | `System.String::TrimStart()` | precision bug, P2-174 |
  | powershell-19687 | `CommandSearcher::setupPathSearcher()` | Equivalent, bounded | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | powershell-19687 | `ConsoleHostUserInterface::WriteChoiceHelper(...)` | Equivalent, bounded | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | powershell-19687 | `GetExperimentalFeatureCommand::GetValidModuleFiles(...)` | Unknown, opaque | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | powershell-19687 | `ModuleCmdletBase::GetModuleForNames(...)` | Unknown, opaque | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | powershell-19687 | `SessionStateInternal::NewItem(string[],string,string,object,CmdletProviderContext)` | Unknown, opaque | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | powershell-19687 | `NativeCommandProcessor::Complete()` | Unknown, timeout | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | powershell-19687 | `NewPSDriveCommand::ProcessRecord()` | Unknown, timeout | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | gitextensions-11372 | `GitCommands.PathUtil::GetRepositoryName(string)` | Equivalent, bounded | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |
  | gitextensions-11372 | `GitExtensions.Program::Main()` | Unknown, abstraction | constant | `ThisAssembly.Git::Sha` | right to be out: the build-generated commit constant differs between any two commits (ADR 0046 keeps it an ordinary constant) |
  | gitextensions-11284 | `CopyContextMenuItem::AddItem(string,string,System.Drawing.Image,char?)` | Equivalent, bounded | callee | `System.String::TrimEnd(char)` | precision bug, P2-174 |

- Ten of the 11 are one precision bug. Neither side of any of them calls the member the property
  names: each calls `TrimEnd(params char[])` or `TrimStart(params char[])` with several characters
  or with an array, and the legacy side's text writes that call under the modern name of the
  API-equivalence entry for the one-character (or no-character) call, which the modern side's text
  does not. The fix decides when the text applies a member entry, which is not a few lines: P2-174.
  No soundness doubt: the rename cannot make two texts equal, since the argument under the call
  differs.
- The ticket's guess, that a body calling one of the 17 `unbound` bodies (P2-172) may not be
  congruent, is wrong for all 11. `GitExtensions.Program::Main()` is the same case as
  `BugReporter.Program::Main()`, the commit constant P2-110 set aside; the two results with
  component `constant` on each Git Extensions pair are those two procedures.
- Toolchain: this box's Git Bash fails to parse a heredoc whose body has an odd number of
  apostrophes; scripts were written to files.
