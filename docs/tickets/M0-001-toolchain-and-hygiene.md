# M0-001 Toolchain install and repo hygiene
Status: in-progress
Effort: S
Depends on: nothing

## Goal
The Windows dev box can build .NET 10 and evaluate legacy csproj; the repo has the
baseline hygiene files and its default branch is `main`.

## Deliverables
- [ ] Install with winget: .NET 10 SDK (latest patch); VS 2026 Build Tools with the
      ".NET desktop build tools" workload plus ".NET Framework 4.8 targeting pack" and
      ".NET Framework 4.8 SDK" components; Docker Desktop; GitHub CLI. Record exact
      versions in Notes.
- [ ] `git config user.name` / `user.email` set (ask the user for the values; do not invent).
- [ ] `git branch -m master main`; initial commit of the planning docs.
- [ ] `global.json` pinning the SDK major (`"version": "10.0.0", "rollForward": "latestFeature"`)
      and `"test": {"runner": "Microsoft.Testing.Platform"}`.
- [ ] `dotnet.config` with `[dotnet.test:runner]` / `name = "Microsoft.Testing.Platform"`.
- [ ] `.gitignore` (dotnet template + `TestResults/`, `StrykerOutput/`, `.vs/`),
      `.gitattributes` (`* text=auto`, `*.verified.* -text`).
- [ ] Verify: `dotnet --version` prints 10.x; `MSBuild.exe` under
      `Program Files\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin` answers `-version`;
      net48 reference assemblies exist under
      `Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`.

## Out of scope
No project files yet (M0-002).

## Notes
