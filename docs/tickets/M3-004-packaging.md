# M3-004 Packaging: single-file publish, container, GitHub Action, release
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-003

## Goal
Anyone can run `equiv` without cloning: a single-file binary per OS from a GitHub
release, a container image, and a GitHub Action that uploads the SARIF to Code Scanning.
(Making Stryker a required check moved to M0-011, done before M3.)

## Spec references
QUALITY-GATES.md (packaging row, mutation row); ADR 0006 (container is the free tier).

## Acceptance criteria (all must hold; nothing beyond them)
1. `dotnet publish src/Equiv.Cli -r win-x64` and `-r linux-x64` with
   `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`
   (Z3 native), produce one executable each; `equiv --version` prints the MinVer version.
   The linux binary runs and exits 3 with the message "no frontend supports these
   inputs on this platform" when given a `.sln` (loader unsupported off Windows).
2. `Dockerfile` (multi-stage, `mcr.microsoft.com/dotnet/sdk:10.0` build,
   `mcr.microsoft.com/dotnet/runtime-deps:10.0` final) builds `equiv:<version>`;
   `docker run equiv --version` works. Image size is recorded in Notes.
3. `action.yml` at repo root: inputs `legacy`, `modern`, `config`, `baseline`,
   `fail-on`; runs the container (`runs.using: docker`); output `sarif` path; a
   documented follow-up step in README shows `github/codeql-action/upload-sarif` with
   `category: equiv`.
4. `.github/workflows/release.yml`: on tag `v*`, publish both binaries, build and push
   the image to GHCR, attach binaries to the GitHub release. Actions pinned by SHA.
5. (Moved to M0-011: `mutation.yml` is blocking at `--break-at 90`. Nothing to do here.)
6. `build.ps1` unchanged.
7. Licensing (ADR 0017, M0-009). `Equiv.Cli` sets `IsPackable` explicitly if it needs to be
   packable — `Directory.Build.props` now defaults it to `false` so nothing publishes by
   accident. `LICENSE` and `THIRD-PARTY-NOTICES.md` are present in the container image and
   alongside each released binary. `action.yml` states the licence. The GitHub release body
   states that `equiv` is BUSL-1.1, source-available and not open source, and links `LICENSE`.
8. The released version gets its own Change Date. BUSL applies separately to each version, so
   `release.yml` stamps the `Change Date` of the published artifact's `LICENSE` to four years
   from the release date rather than shipping the repo's placeholder unchanged.
9. MSBuild redistribution is resolved and the resolution is written in Notes. VS Build Tools is
   **not** freely redistributable inside an image, so the container must either rely on the .NET
   SDK's own redistributable MSBuild or use a Microsoft-published build-tools base image under
   its per-container EULA. If neither is workable, the container ships without legacy `.csproj`
   support and README says so — shipping an image that embeds non-redistributable Microsoft
   build tooling is not an option.

## Files
`src/Equiv.Cli/Equiv.Cli.csproj` (publish properties), `Dockerfile`, `.dockerignore`,
`action.yml`, `.github/workflows/release.yml`,
`README.md`, `docs/QUALITY-GATES.md`, `LICENSE` (Change Date stamping in `release.yml` only;
the checked-in file keeps its placeholder date).

## Tests
`Equiv.Cli.Tests`: `Version_PrintsMinVer`. Everything else is verified by running the
commands in criteria 1 and 2 locally and pasting the output into Notes; the release
workflow is verified by pushing a `v0.1.0-rc.1` tag (ask the user before tagging).

## Size guard
No code changes in `src/` beyond `--version` and the platform message.

## Out of scope
Hosted tier, API keys, Helm charts, licence enforcement or any runtime licence check,
SonarQube upload (the SARIF path is already
consumable by `sonar.sarifReportPaths`; document it in README, do not integrate).

## Notes
