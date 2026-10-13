# P2-173 On a same-runtime pair, a result says why a body in a byte-identical file is not congruent
Status: todo
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
