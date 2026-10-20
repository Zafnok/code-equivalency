# P2-261 Precision bug: a path row fires on a path the runtime or the solution made, which cannot hold a character .NET Framework rejects
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074's `ValidPaths` reads a constant, a guarded value and the forms P2-207, P2-208, P2-228 and P2-231 added as valid
paths. P2-247 (`docs/runs/2026-10-20-precision-execute-verdict-2.md`) found 8 false results on paths that come from a
source that can only produce a path Windows accepted, joined with constants:
- the executing assembly's directory (`Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)` and the like):
  Duplicati `VssBackupComponentsHelper::CreateVssBackupComponents()` (EQ006), `Acknowledgements::GET(string,RequestInfo)`
  (exposure);
- a special folder or the temp folder (`Environment.GetFolderPath`, `Application.UserAppDataPath`, `Path.GetTempPath`):
  Git Extensions exposures `W08`, `U08`, `U12` of the verdict's appendix C;
- a file the solution itself created (a `TempFile` the volume writer or the encryption step wrote): Duplicati exposures
  `SpillCollectorProcess::Run(...)` and `BackendUploader::DoPut(...)`;
- a field the constructor set through `Path.GetFullPath`, which already rejects the characters on .NET Framework:
  Git Extensions `GitTagController::CreateTag(...)` (EQ006, through `GitModule.WorkingDir`).
When done, each of these sources is a valid path (and a constant joined to one by `Path.Combine` stays one), or
`equiv-adr` records why a source is not.

## Spec references
`ValidPaths` (P2-074), tickets P2-207, P2-208, P2-228, P2-229 (a directory listing, the sibling source), P2-231,
`docs/runs/2026-10-20-precision-execute-verdict-2.md` (appendices A and C).

## Acceptance criteria (all must hold; nothing beyond them)
1. Unit tests, one per source above, show the path row does not fire; a parameter joined to the same source still does.
2. `gitextensions-8522` and `duplicati-3124` in `full`: each of the 8 results is given with its result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247. A path made from `Directory.GetFiles`/`EnumerateFiles` is P2-229's (three more results).
