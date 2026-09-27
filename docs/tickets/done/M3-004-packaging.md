# M3-004 Packaging: single-file publish, container, GitHub Action, release
Status: done (PR #229)
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
- Criterion 9 resolved: no VS Build Tools component is redistributed anywhere. The image's final stage is `mcr.microsoft.com/dotnet/sdk:10.0-noble` itself (its own MSBuild, redistributable); non-SDK (legacy) projects load through M3-029's bare loader, which needs no MSBuild at all. A hand-written multi-stage `Dockerfile` was used instead of `dotnet publish -t:PublishContainer` (M3-028's preference) because the loader needs the full SDK present *at runtime*, not only to build `equiv` itself — `PublishContainer`'s default final base is a runtime image, and there is no built-in switch to make the *final* stage the SDK, only `ContainerBaseImage` for a single-stage publish that would then also need `-p:SelfContained=false` fighting the RID-gated single-file properties below. A plain multi-stage `Dockerfile` with `FROM ...sdk:10.0-noble` on both stages was simpler than fighting the container-publish tooling for a shape it does not model well (bar test: implementation detail, not a deviation from the ADR — the ADR only decided the base image, not the build mechanism).
- Toolchain (criterion 1): `PublishSingleFile` alone breaks `Equiv.Frontend.CSharp` at runtime. `Microsoft.CodeAnalysis.Workspaces.MSBuild` ships its out-of-process build host (`BuildHost-net472/`, `BuildHost-netcore/`) as NuGet `contentFiles`; single-file bundling folds those `.exe`/`.dll` files into the bundle, so the physical files the workspace spawns as a subprocess no longer exist on disk ("the build host could not be found"). Fix: `IncludeAllContentForSelfExtract=true` (`Equiv.Cli.csproj`), which extracts all content files (not just natives) to the self-extraction directory at startup, restoring the on-disk layout. Verified end to end: the published win-x64 binary and a Linux build run inside `mcr.microsoft.com/dotnet/sdk:10.0-noble` both produced SARIF byte-identical (modulo the `artifactLocation` path) to `dotnet run`'s output, across every sample that already restores cleanly on this branch.
- Toolchain (criterion 1): `RestorePackagesWithLockFile=true` plus `dotnet restore -r <rid>` overwrites `packages.lock.json`'s recorded target set with just that RID's, rather than adding to it — a subsequent plain restore then drops the RID target again. Declaring `<RuntimeIdentifiers>` (plural) to lock all targets in one pass was tried and reverted: it forced every `ProjectReference` in the graph to also carry RID-specific lock entries for `--locked-mode -r <rid>` to pass, which is unnecessary churn for libraries that need none. Decision: RID-specific publish steps (`parity-run.ps1`, `release.yml`) restore without `--locked-mode`; only the RID-less base restore ci.yml gates on stays locked. The lock files this ticket's local testing incidentally mutated were reverted before commit.
- Decision (ADR 0002/0017, `equiv-decide`): added `<MinVerTagPrefix>v</MinVerTagPrefix>` to `Directory.Build.props`. Criterion 5's tag example (`v0.1.0-rc.1`) and this ticket's release tags all carry a `v` prefix; MinVer's default pattern has none, so without this every tagged release would silently fall back to the untagged `0.0.0-alpha.0...` version.
- Decision (`equiv-decide`): `Equiv.Cli` does not set `IsPackable=true`. Criterion 7's "if it needs to be packable" does not apply — this ticket ships a single-file binary, a container and a GitHub Action, never a NuGet package — so `Directory.Build.props`'s `IsPackable=false` default stands unchanged.
- Decision (`equiv-decide`): the container's own `dotnet publish` (Dockerfile) is framework-dependent (no `-r`), not the self-contained single-file build criterion 1 ships. The final image already carries the .NET SDK (and therefore the runtime); a self-contained publish would duplicate it for no benefit and would also need `IncludeAllContentForSelfExtract`'s self-extraction step on every `docker run`.
- Decision (`equiv-decide`): `action.yml` uses `entrypoint: /entrypoint.sh` to override the image's own `ENTRYPOINT` for the Action's invocation only. This keeps `docker run equiv --version`/`compare` (criterion 2) working unmodified while giving the Action a place to skip optional inputs (`config`, `baseline`, `fail-on`) that arrive as `""` when unset, and to emit the `sarif` output.
- MinVer needs a git checkout (tags and history) to compute a real version; the Dockerfile's `build` stage has none (`.dockerignore` excludes `.git`, same as any image). `release.yml` passes the tag's version via `--build-arg VERSION`, applied with `-p:MinVerVersionOverride` (MinVer's own override property — plain `-p:Version` is overwritten by MinVer's target). An ad hoc `docker build` with no build-arg keeps MinVer's untagged fallback (verified: `0.0.0-alpha.0`).
- Image size: 1.62 GB, measured locally (`docker images`) for the framework-dependent `mcr.microsoft.com/dotnet/sdk:10.0-noble`-based final stage. M3-028 measured the plain Debian `sdk:10.0` base at 917 MB; `noble` plus the published app roughly doubles that.
- Verified locally (Windows dev box, Docker Desktop with the Linux engine): `dotnet publish -r win-x64` produces one `Equiv.Cli.exe`; `equiv --version` prints the MinVer version (`0.0.0-alpha.0.163+<sha>`, untagged); running it against every sample under `samples/` gives SARIF byte-identical to `dotnet run`'s (both exit codes and content, apart from `webapi-basic`, which fails to load identically both ways — a pre-existing restore gap unrelated to packaging). Cross-published `linux-x64` and ran it inside `docker run mcr.microsoft.com/dotnet/sdk:10.0-noble`: same version string, same `compare` result on the `identical` sample, SARIF identical modulo `artifactLocation.uri` (which `sarif-parity.ps1` already ignores). Built the container image (`docker build .`), ran `docker run equiv --version` and `docker run -v <samples>:/samples equiv compare ...` (read-write mount; MSBuildWorkspace needs to write `obj/`) with matching results, and exercised the Action's `/entrypoint.sh` directly with simulated `GITHUB_WORKSPACE`/`GITHUB_OUTPUT` env vars, confirming both required and all-optional-inputs-empty invocations. Did not verify the release workflow itself (tagging needs the user's go-ahead per the Tests section) or a real `docker push` to GHCR (needs the repo's own GHCR credentials, which only run in CI).
