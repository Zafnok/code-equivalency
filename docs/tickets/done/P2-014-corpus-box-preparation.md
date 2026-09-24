# P2-014 `corpus.ps1` prepares a box: long paths, submodules, isolation, reference assemblies, SDK resolver
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-022

## Goal
M3-022 needed a string of manual workarounds before any pair would load. They are all in
`tools/corpus/` territory, not `src/`. Each is recorded in the run's SUMMARY.md files:
1. `-Fetch` fails with "Filename too long" in a worktree path. It then reports `reuse` on the
   half-checked-out directory on the next call. The census passed `core.longpaths=true` through
   `GIT_CONFIG_COUNT`/`GIT_CONFIG_KEY_0`/`GIT_CONFIG_VALUE_0` and deleted the partial clones by hand.
2. Git Extensions needs `git submodule update --init` (`Externals/`).
3. `.corpus/` is inside this repository, so MSBuild picks up this repo's `Directory.Build.props`,
   `Directory.Packages.props` (NU1008 on central package management), `.editorconfig` and
   `global.json`. The last one forces the Microsoft.Testing.Platform runner onto `dotnet test`.
   The census wrote empty sentinel `Directory.Build.props`, `Directory.Build.targets`,
   `Directory.Packages.props` (central management off) and `.editorconfig` (`root = true`) into
   `.corpus/`. A sentinel `global.json` was tried and then removed while the SDK-resolver problem
   (item 5) was being diagnosed, so this repo's test-runner setting still reaches the corpus. One
   migrating agent added its own `global.json` to get around it.
4. VS 2026 Build Tools ships reference assemblies only for 4.7.2 and 4.8, and its installer rejects
   `Microsoft.Net.Component.4.6.1.TargetingPack` (exit 87). The census restored
   `Microsoft.NETFramework.ReferenceAssemblies.net20` through `net481` (1.0.3) into
   `.corpus/refasm/` and set `TargetFrameworkRootPath` to it.
5. The Build Tools MSBuild has no .NET SDK resolver, so SDK-style `net4x` projects fail with
   "SDK 'Microsoft.NET.Sdk' could not be found". The census set
   `MSBuildSDKsPath=<dotnet>/sdk/<version>/Sdks` and `MSBuildEnableWorkloadResolver=false`.
6. Git Extensions' modern `global.json` pins SDK 5.0.202 with no roll-forward. The census added
   `rollForward: latestMajor` to the checkout's copy.
7. Restores need `NuGetAudit=false`, plus `NoWarn=NU1701;NU1702;NU1903` until P2-012 lands, and a
   forced restore.
8. `-Unchanged` accepts `owner/name` for agent pairs but not the `pmb-...` slug that `-Fetch` and
   `pair.json` print.

After this ticket, a `-Prepare` switch does 3 and 4 once per box, `-Fetch` does 1, 2 and 6, and a
`-Env` switch prints the environment block for 4, 5 and 7, which the skill's run step uses.

## Spec references
`tools/corpus/corpus.ps1`; `.claude/skills/equiv-corpus-run/SKILL.md` sections 3 and 5; ADR 0028.

## Acceptance criteria (all must hold; nothing beyond them)
1. On a fresh clone in a path over 100 characters, `-Prepare` then `-Fetch gitextensions-8522`
   leaves both checkouts clean (`git status --porcelain` empty), with submodules.
2. `-Fetch` refuses to `reuse` a checkout whose `HEAD` does not resolve or whose status is dirty.
3. The skill's section 5 uses `-Env` and no longer needs any step listed above by hand.
4. `-Unchanged` accepts the `pmb-...` slug.

## Size guard
`tools/corpus/` and the skill only. No `src/` changes.

## Out of scope
Installing Visual Studio components.

## Notes
- Decision: `-Prepare` writes only `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props` and `.editorconfig` sentinels under `.corpus/`, no `global.json`
  sentinel, per item 3's note that one was tried and removed. `-Fetch` still patches a checkout's
  *own* `global.json` (item 6, e.g. Git Extensions' modern side) — a different file, same ticket.
- Decision: reference assemblies (item 4) come from the `Microsoft.NETFramework.ReferenceAssemblies.*`
  NuGet packages (net20 through net481, 1.0.3), restored once via a throwaway SDK-style csproj and
  copied from each package's `build/.NETFramework/v<X>/` into `.corpus/refasm/.NETFramework/v<X>/`,
  which is exactly the layout `TargetFrameworkRootPath` (`-Env`) expects. Idempotent: `-Prepare`
  checks all 14 folders exist before restoring again.
- Decision: `core.longpaths=true` is passed via `-c` on every git invocation (not set once via
  `git config`), because `-c` propagates to the child git processes `submodule update` spawns, so
  submodules on a >100-char path get it too without a second mechanism.
- Toolchain: patching a checkout's `global.json` (item 6) leaves it `git status`-dirty, which broke
  acceptance criterion 1 during testing (the modern Git Extensions checkout showed `M global.json`
  after `-Fetch`). Fixed by `git update-index --skip-worktree global.json` right after the patch.
- Toolchain: `git submodule update --init`'s stderr chatter ("Submodule '...' registered for
  path...") intermittently made Windows PowerShell 5.1 promote it to a terminating
  `NativeCommandError` under this script's `$ErrorActionPreference = 'Stop'`, even though git's own
  exit code was 0 — reproduced only on some runs, not every one. Fixed by routing `Invoke-Git`'s
  git call through `2>&1 | ForEach-Object { "$_" }` with `$ErrorActionPreference = 'Continue'` for
  the duration of the call, so stderr lines become plain text instead of error records; the real
  success/failure signal stays `$LASTEXITCODE`.
- Verified against the real corpus on this box (not just parsed): fresh `-Prepare` restored the 14
  reference-assembly packages and was a no-op reuse on a second run; `-Fetch gitextensions-8522`
  left both checkouts (`.corpus/repos/gitextensions__gitextensions@3f4ed21998af` and `@5190ba5c1a5f`,
  under this worktree's path, over 100 characters) with `git status --porcelain` empty and
  `Externals/` submodules populated; a checkout dirtied by hand was refused with a clear error and
  reused cleanly once restored; `-Unchanged pmb-shiningrush__serviceant` (the exact slug `-Fetch`
  and `pair.json` print) resolved and ran, alongside the pre-existing `-Unchanged ShiningRush/ServiceAnt`
  form. All corpus artifacts from this testing were deleted afterwards (`.corpus/` stays untracked).
