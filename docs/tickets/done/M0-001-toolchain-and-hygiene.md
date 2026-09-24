# M0-001 Toolchain install and repo hygiene
Status: done (PR #1)
Effort: S
Depends on: nothing

## Goal
The Windows dev box can build .NET 10 and evaluate legacy csproj; the repo has the
baseline hygiene files and its default branch is `main`.

## Deliverables
- [x] Install with winget: .NET 10 SDK (latest patch); VS 2026 Build Tools with the
      ".NET desktop build tools" workload plus ".NET Framework 4.8 targeting pack" and
      ".NET Framework 4.8 SDK" components; Docker Desktop; GitHub CLI. Record exact
      versions in Notes.
- [x] `git config user.name` / `user.email` set (ask the user for the values; do not invent).
- [x] `git branch -m master main`; initial commit of the planning docs.
- [x] `global.json` pinning the SDK major (`"version": "10.0.0", "rollForward": "latestFeature"`)
      and `"test": {"runner": "Microsoft.Testing.Platform"}`.
- [x] `dotnet.config` with `[dotnet.test:runner]` / `name = "Microsoft.Testing.Platform"`.
- [x] `.gitignore` (dotnet template + `TestResults/`, `StrykerOutput/`, `.vs/`),
      `.gitattributes` (`* text=auto`, `*.verified.* -text`).
- [x] Verify: `dotnet --version` prints 10.x; `MSBuild.exe` under
      `Program Files\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin` answers `-version`;
      net48 reference assemblies exist under
      `Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`.

## Out of scope
No project files yet (M0-002).

## Notes

Installed via `winget` (all `--source winget`, to avoid an interactive msstore
Terms-of-Transaction prompt that blocks non-interactive shells):
- .NET 10 SDK: `Microsoft.DotNet.SDK.10` → 10.0.401 (`dotnet --version` / `--list-sdks`
  confirm 10.0.401 under `C:\Program Files\dotnet\sdk`).
- VS 2026 Build Tools: `Microsoft.VisualStudio.BuildTools` → 18.10.1, installed with
  `--override "--wait --quiet --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.Net.Component.4.8.TargetingPack --add Microsoft.Net.Component.4.8.SDK"`.
- Docker Desktop: `Docker.DockerDesktop` → 4.91.0 (`docker --version` reports 29.8.0
  engine/CLI). Desktop app itself has not been launched/signed in this session — that's
  a one-time interactive step for whoever uses this box, not something scriptable here.
- GitHub CLI: `GitHub.cli` → 2.101.0.

Path correction vs. the ticket text: on this box VS 2026 Build Tools installs under
`C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\...`, not
`C:\Program Files\Microsoft Visual Studio\18\BuildTools\...` (Build Tools is a 32-bit
installer target even though the toolset itself is fine). `MSBuild.exe -version` from
that x86 path answers `18.10.1.42706`. net48 reference assemblies are present under
`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`
(same x86 prefix).

A freshly-installed winget package isn't on `PATH` in a shell that was already open;
each tool needed `$env:PATH` rebuilt from the Machine+User env vars in-process before
it resolved (`gh`, `docker`). Not an installer problem — just note it if a later ticket's
script assumes a fresh shell.
