# M3-004 Packaging: single-file publish, container, GitHub Action, release
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-003, M3-027, M3-029

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
   On `ubuntu-latest` with the .NET 10 SDK installed, the linux-x64 binary runs `equiv compare`
   on every sample pair (M3-029's loader), and its SARIF results equal the win-x64 binary's
   modulo paths. M3-029's `parity` job runs against the two published binaries, not only
   `dotnet run`.
2. The image builds `equiv:<version>` on an Ubuntu 24.04 .NET SDK base
   (`mcr.microsoft.com/dotnet/sdk:10.0-noble` or the SDK container tooling's equivalent). It needs
   the SDK because SDK-style projects load through the SDK's MSBuild (ADR 0031 Clarification
   2026-09-25), and it needs noble for `libz3`'s glibc 2.38 floor. `runtime-deps` is not enough.
   `docker run equiv --version` works, and `docker run` with `samples/` mounted analyses every
   sample pair with the same results as criterion 1. The image holds no net4x reference
   assemblies or packages (M3-029 fetches them into a cache). README documents the volume for
   that cache. Image size is recorded in Notes (M3-028 measured the Debian `sdk:10.0` at 917 MB).
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
- Note (from the M3-001 review, 2026-09-21): `Microsoft.Z3` 4.12.2 ships no `runtimes/linux-x64` native, so criterion 1's `IncludeNativeLibrariesForSelfExtract` has no Linux `libz3.so` to bundle, and the Docker image lacks one too. CI takes it from the pinned PyPI `z3-solver==4.12.2.0` manylinux wheel (`.github/workflows/ci.yml`, "Provide libz3 (Linux)"); this ticket must choose how the linux-x64 artifact and the image get it (same wheel, or a source build) and verify the Linux binary actually loads it. ADR 0002's Microsoft.Z3 row records the gap.
- Note (2026-09-23, ADRs 0030 and 0031): this ticket now also depends on M3-027 (Z3 5.1 ships `libz3.so` for linux-x64, which answers the note above) and on M3-029 (Linux loader). M3-028 rewrites criteria 1 and 2 once the loader mechanism is chosen: the Linux binary and the container must analyse the samples, not exit 3, and the final image must be Ubuntu 24.04-based (glibc 2.38 floor for `libz3`). Prefer `dotnet publish -t:PublishContainer` over a hand-written Dockerfile unless the chosen loader needs packages the SDK container tooling cannot add.
- Note (M3-027, 2026-09-24): the Linux `libz3` question above is answered. `Microsoft.Z3` 5.1.0 (ADR 0030) ships `runtimes/linux-x64/native/libz3.so` in the package itself; the PyPI wheel workaround is gone from every workflow. `IncludeNativeLibrariesForSelfExtract` (criterion 1) now has a native to bundle without any extra step here.
- Note (M3-028, 2026-09-25): criteria 1 and 2 were rewritten for ADR 0031's chosen loader (candidate 2). This also answers criterion 9 in part. The image carries only the .NET SDK's own MSBuild (redistributable), which is used for SDK-style projects. Non-SDK projects load with M3-029's bare loader and need no MSBuild, so no VS Build Tools component goes into the image. Criterion 9's Notes entry should say so, citing the Clarification. With `PublishContainer`, set `ContainerBaseImage` to the noble SDK image. The default base for a self-contained app is `runtime-deps`, which lacks the SDK.
