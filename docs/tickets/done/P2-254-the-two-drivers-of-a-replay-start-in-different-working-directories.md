# P2-254 Execution bug: the two drivers of a replay or `probe` start in different working directories, so an outcome that holds the working directory is `reproduced` on any runtime pair
Status: done (PR #553)
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
- Decision: a host bound by `Within` starts every process in one `cwd-<random>` folder under its directory (a
  `WorkingDirectory` held by the bound host), emptied whenever a process starts there while no other runs, instead of
  one fresh folder per process. Alternative: one folder per case, handed from `Replayer` to the host. Rule: 2 (the
  smallest change: `IDriverHost` and every caller stay as they are). Replay and `probe` run their sides one after the
  other, so each side still starts in an empty folder, at the same path; differential testing's two streams run at
  once in the shared folder, as both did in the caller's directory before P2-040. P2-040's
  `DriverWorkingDirectoryTests` pass unchanged, since the folder is still `cwd-<random>`.
- Decision: when the folder cannot be emptied (a file a killed process tree still holds), what is left stays, rather
  than end the run. Alternative: throw, losing every later result of the run. Rule: 2 (it is rare, since disposing a
  session waits up to 10 seconds for the killed process to exit, and a leftover file is the state every driver ran in
  before P2-040).
- Decision: criterion 1's `samples/` pair is a new sample, `relative-full-path` (`Paths.Full(string)` returns
  `Path.GetFullPath`), as P2-240 added `package-runtime-asset`; its compare verdict is EQ007, since
  `Path.GetFullPath`'s path-validation row applies to a non-constant argument. The probe test is in `ProbeTests`.
- Criterion 2: `full --execute` of both pairs at `a537dffc` (compare mode quick, `--jobs 4`, the pairs' committed
  configs, P2-237's checkouts and restores as P2-245 used them; Duplicati 614 s, Git Extensions 355 s, exit 1 each), set
  against P2-245's runs at `bceeea62`. Each of the three is no longer `reproduced`, so it stays EQ007 (ADR 0060 decision
  4 promotes only a `reproduced` one to EQ006):
  - Duplicati `SystemIOLinux::PathGetFullPath(string)`: `not-applicable`; both sides return
    `<root>\cwd-<x>\s0`, the same path.
  - Duplicati `Utility.Uri::UriBuilder(string,string,NameValueCollection)` and Git Extensions
    `GitModule::GetSubmoduleFullPath(string)`: `not-constructible`, "the call traces differ, and the model's outcomes
    rest on call answers chosen after they split, which replay does not observe" (P2-037's alike reason, which
    `Replayer` gives only when the two sides' outcomes are equal).
  No other replay of either pair changed: Duplicati `reproduced` 12 to 10, `not-applicable` 8 to 9,
  `not-constructible` 98 to 99; Git Extensions `reproduced` 8 to 7, `not-constructible` 127 to 128, of 118 and 145
  replays. Raw SARIF: `.corpus/pairs/<slug>/runs/20261019-1221-*` and `20261019-1231-*` of P2-237's worktree, through
  this worktree's `.corpus` junction, as P2-245 did.
- Note: a fresh worktree fails 132 integration cases locally, every one on one of the 12 SDK-style samples it never
  restored (`dotnet restore` per sample fixes it); CI restores them. A corpus run without `corpus.ps1 -Env` loaded
  exits 4 on Duplicati (no net471 reference assemblies) and Git Extensions.
