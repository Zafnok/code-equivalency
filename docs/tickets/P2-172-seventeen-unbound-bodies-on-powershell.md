# P2-172 Seventeen bodies of `powershell-19687` are `unbound` on both sides of a human commit
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-106

## Goal
P2-106 took `powershell-19687`'s `unbound` Unknowns from 82 to 17: a body whose only diagnostics
are warnings promoted to errors binds. The 17 it left are still there in P2-110's run at equiv
`78853202` (`docs/runs/2026-10-13-cleanup-powershell-19687/SUMMARY.md`), the same 17 methods on both
sides, all in files the pull request did not touch. They are 17 of the pair's 37 changed pairs and
17 of its 27 Unknowns. Both sides are commits a person built, so each is a finding (ADR 0028,
clarified 2026-10-01), and no ticket owns them.

The 17, by procedure identity. In `System.Management.Automation` (12):
- `Microsoft.PowerShell.Commands.PSSessionConfigurationCommandUtilities::GetWinrmPluginShellName()`
  and `::GetWinrmPluginDllPath()`
- `Microsoft.PowerShell.Telemetry.ApplicationInsightsTelemetry::SendPSCoreStartupTelemetry(string,double)`
- `System.Management.Automation.PowerShellAssemblyLoadContext::.ctor(string)`
- `System.Management.Automation.PSVersionInfo::.cctor()`
- `System.Management.Automation.AmsiUtils::Init()`
- `System.Management.Automation.Remoting.ClientRemoteSessionImpl::RunClientNegotiationAlgorithm(System.Management.Automation.Remoting.RemoteSessionCapability)`
- `System.Management.Automation.Remoting.ClientRemoteSessionDSHandlerImpl::ProcessSessionMessages(System.Management.Automation.RemoteDataEventArgs)`
- `System.Management.Automation.Remoting.WSManPluginInstance::EnsureOptionsComply(System.Management.Automation.Remoting.Client.WSManNativeApi.WSManPluginRequest)`
  and `::EnsureProtocolVersionComplies(...)`
- `System.Management.Automation.Remoting.ServerRemoteSession::RunServerNegotiationAlgorithm(System.Management.Automation.Remoting.RemoteSessionCapability,bool)`
- `System.Management.Automation.Remoting.ServerRemoteSessionDSHandlerImpl::RaiseDataReceivedEvent(System.Management.Automation.RemoteDataEventArgs)`

In `Microsoft.PowerShell.ConsoleHost` (5):
- `Microsoft.PowerShell.ConsoleHost::Start(string,string,bool)`
- `Microsoft.PowerShell.UnmanagedPSEntry::Start(string[],int)`
- `Microsoft.PowerShell.UpdatesNotification::.cctor()`, `::CheckForUpdates()` and
  `::QueryNewReleaseAsync(System.Management.Automation.SemanticVersion)`

Several of these read a version or build constant (`PSVersionInfo`, the update check, the startup
telemetry), so one guess is a generated source file the corpus steps do not produce: the pair
already needs the repository's resource generator run by hand (`tools/corpus/README.md`). That is a
guess; the SARIF does not record which diagnostic unbound a body.

When done, either the 17 bind, or it is written down per diagnostic why a body that a person
compiled does not bind here and what the run needs for it to.

## Spec references
`docs/tickets/done/P2-106-*.md` (the Notes: how the 82 were counted and what the rule is now);
ADR 0029 (`unbound`, clarified 2026-10-01); ADR 0028 (same date);
`tools/corpus/README.md` (the three manual steps of this pair); `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: for each of the 17, the ids of the error-severity diagnostics in its body on each
   side, grouped. Record the table in Notes (ids and counts, no source text).
2. For each group, one of: a corpus step that makes the missing symbol exist (written into
   `tools/corpus/README.md`, and into `corpus.ps1 -Fetch` if it can be done without editing a
   tracked file of the checkout); a fix in the loader, with a test; or a line in Notes saying why
   the body cannot bind on this box.
3. A result that is `unbound` names, in its SARIF properties, the ids of the diagnostics that
   unbound it, so the next count needs no hand build. `docs/VERIFICATION-MODEL.md`, where the result
   properties are documented, gains the property. This is a public surface: add the `Release:` footer
   `equiv-release` says.
4. Rerun `powershell-19687` in compare mode quick. Notes record the `unbound` count next to 17.

## Tests
- A unit test for criterion 3's property on a body with one error diagnostic.
- Whatever criterion 2's loader fix needs, if there is one.

## Size guard
A cause that needs PowerShell's full build (its `Start-PSBuild`) to exist: record it and stop; that
is a corpus decision, not this ticket's.

## Out of scope
Changing what `unbound` means (P2-106 settled it); the other pairs.

## Notes
- Filed 2026-10-13 from P2-110's rerun of the cleanup pairs. No open ticket owned the 17.
- Criterion 1, counted 2026-10-13 from `properties.unboundDiagnostics` of a quick compare run on this branch (193
  seconds, exit 0), fetched with the three steps the README had. 17 `unbound`, the same 17, and on every one the legacy
  side and the modern side have the same ids and counts. Error-severity diagnostics per side:

  | Group | Bodies | Diagnostics per side | What does not exist |
  |---|---|---|---|
  | CS0117 | 15 | 22 | `PSVersionInfo.ProductVersion` or `PSVersionInfo.GitCommitId`, read from another type |
  | CS0103, in `PSVersionInfo::.cctor()` | 1 | 9 | `GitCommitId` and the `Version_*` constants, read inside the type |
  | CS0103, in `PowerShellAssemblyLoadContext::.ctor(string)` | 1 | 1 | the method `InitializeTypeCatalog()` |

  By body, CS0117 per side: 4 in `ServerRemoteSession::RunServerNegotiationAlgorithm`, 3 in
  `ClientRemoteSessionImpl::RunClientNegotiationAlgorithm`, 2 each in `AmsiUtils::Init()` and
  `WSManPluginInstance::EnsureOptionsComply`, 1 in each of the other 11 (all five `ConsoleHost` bodies among them).
  No body has two ids, and no cause is `none`.
- Criterion 2, the first two groups (16 bodies): a corpus step. The constants are declared by the repository's own
  source generator, a project `System.Management.Automation` references as an analyzer. Two things kept it from
  running. Its assembly was not on disk, because nothing in the corpus steps builds it. And once built it throws: it
  parses three MSBuild properties that PowerShell's build computes from `git describe` in a target that runs before a
  real build and not when a workspace loads the project, so they were empty. A run with the generator built and the
  properties unset still had all 17 (259 seconds, exit 0). Steps 4 and 5 of `tools/corpus/README.md` build the
  generator and set the three properties in the environment. P2-106's guess was right about the generator and did not
  see the second half.
- Criterion 2, the third group (1 body): a corpus step. `InitializeTypeCatalog()` is declared in
  `CorePsTypeCatalog.cs`, which the repository's `Start-TypeGen` writes and git ignores. Step 6 of the README does what
  that function does: one design-time MSBuild target that lists the references, then `dotnet run` in
  `src/TypeCatalogGen`.
- Size guard: not tripped. None of the three new steps is `Start-PSBuild` or compiles a product assembly. Two are the
  pieces of it that write source (as the resource generator of step 3 already was) and one sets properties.
  `git status --porcelain` is empty on both checkouts after all six.
- Deviation: the three steps are in `tools/corpus/README.md` and not in `corpus.ps1 -Fetch`, though none edits a
  tracked file. Step 6 needs the projects restored and step 5 is the run's own environment, and `-Fetch` does neither;
  the pair's first three steps are by hand for the same reason, and `corpus.ps1` has no step that belongs to one pair.
  Half of one list in a script and half in a README would be harder to follow than one list.
- Decision: the values of step 5 -> those of a build from the tag of step 2 (`7.4.0-preview.3`), the same on both
  sides, without the commit id a build appends to `ProductVersion`. The two sides are different commits, so the true
  values differ by that id alone, and every body that reads the constant would differ for a reason the pull request
  did not make.
- Criterion 3, the bar test (`equiv-adr`): no ADR and no clarification. The ticket names the property and where it is
  documented; what `unbound` means, the rule ids and the fingerprint do not change.
- Decision: the property -> `properties.unboundDiagnostics`, `{ legacy?, modern? }`, each a list with one id per cause
  of that side in the order of its `relatedLocations`. A list and not a set, so a count needs no second pass, and
  per side, because criterion 1 asks for each side. A cause with no diagnostic of its own (an error type whose
  diagnostic is outside the method) is `none`.
- Decision: how the id reaches the result -> `IrOpaque.Diagnostic`, copied to `UnknownCause.Diagnostic` where the CLI
  turns an `unbound` opaque into a cause. The opaque is the only thing the frontend hands over for a body it does not
  lower. Its reason stays `unbound`, so the census's reason counts and the review groups do not change. The IR text
  spells it `diagnostic "CS0103"` after the span, and no backend reads it.
- Decision: two errors at one span with different ids are two causes now; they were one. Dropping one would drop an
  id from the property. None of the pair's 32 causes per side is such a pair.
- Decision: `Release: minor`. A result property is added and nothing is removed or renamed; `equiv-release` calls a
  backwards-compatible addition to the SARIF minor.
- Criterion 4, the rerun (quick, this branch, all six steps, 230 seconds, exit 0): **`unbound` 0, next to 17.** All 17
  are EQ001 by congruence. By rule: EQ001 34,174 (was 34,156), EQ003 10 (was 27: opaque 3, timeout 3, unaligned-loop
  3, recursion 1, as before). `changedPairs` 20, from 37; `pairsCongruent` 34,164. No other result changed its rule,
  proof method or reason. There is one more pair, 34,184: `PowerShellAssemblyLoadContext::InitializeTypeCatalog()`,
  congruent.
- `samples/partly-compiling-modern/expected.sarif.json` gains the property on its one `unbound` result (`modern`:
  CS0103). Nothing else in it changed.
- Surprising: nothing in a run said a generator had not run. Filed as P2-174.
- Not established: a local run of `Equiv.Tests.Integration`'s `SamplesEndToEndTests`, made to regenerate that
  snapshot while a corpus run was loading on the same box, failed 54 of 258 tests, the ones read showing exit 4 where
  the sample expects 1. It was not rerun alone. CI is the gate.
