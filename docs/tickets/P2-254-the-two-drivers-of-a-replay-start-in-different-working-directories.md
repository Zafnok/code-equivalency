# P2-254 Execution bug: the two drivers of a replay or `probe` start in different working directories, so an outcome that holds the working directory is `reproduced` on any runtime pair
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-040 starts every driver process in a fresh folder, `cwd-<random>`, under the run's working root
(`ChildProcessHost.Start`), so the legacy and the modern driver of one replay or `probe` case run in two different
directories. An outcome that holds the working directory then differs between the sides on any runtimes, and replay
calls it `reproduced`. P2-245's runs (`docs/runs/2026-10-19-execution-reach.md`) have three: Git Extensions'
`GitModule::GetSubmoduleFullPath(string)`, and Duplicati's `SystemIOLinux::PathGetFullPath(string)` and
`Utility.Uri::UriBuilder(string,string,NameValueCollection)`. A `probe` of each with the replay's strings returns
`<root>\cwd-<a>\s1\s2\` on one side and `<root>\cwd-<b>\s1\s2\` on the other. `reproduced` claims that the user can run the
divergence, so a harness difference must never produce one (ADR 0035 decision 2). When done, the two drivers of one
case see one working directory path, and relative writes still never land in the caller's directory (P2-040's goal).

## Spec references
ADR 0035 decision 2, ticket P2-040, `src/Equiv.Execute/ChildProcessHost.cs` (`Start`, `Within`), `IDriverHost`,
`Replayer`, `docs/runs/2026-10-19-execution-reach.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A replay or `probe` whose member returns `Path.GetFullPath` of a relative path gives equal outcomes on both sides
   when the runtimes agree on it; an integration test against a `samples/` pair shows it, and P2-040's
   `DriverWorkingDirectoryTests` still pass.
2. The three replays above are rerun (`full --execute` of both pairs, or `probe` on the replay's inputs), and each one's
   value is given.

## Notes
- Filed 2026-10-19 by P2-245 (a `reproduced` checked by `probe` while hand-tracing criterion 7's `uninitialized` inputs).
  No confirmation in P2-237 or P2-245 rests on a path that holds the working directory: each one is a throw against a
  return or a string that holds no path.
