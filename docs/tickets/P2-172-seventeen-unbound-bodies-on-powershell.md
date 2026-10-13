# P2-172 Seventeen bodies of `powershell-19687` are `unbound` on both sides of a human commit
Status: todo
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
