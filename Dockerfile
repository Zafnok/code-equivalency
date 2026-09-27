# syntax=docker/dockerfile:1
#
# M3-004: the final stage is the .NET SDK image, not a runtime-only one. Equiv.Frontend.CSharp
# loads SDK-style projects through MSBuildWorkspace on the .NET SDK's own MSBuild at runtime
# (ADR 0031 Clarification 2026-09-25, M3-028/M3-029), so the container needs the SDK installed,
# not only the runtime `dotnet /app/Equiv.Cli.dll` itself runs on. `noble` (Ubuntu 24.04) is the
# floor for `libz3.so`'s glibc 2.38 requirement (ADR 0030).
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# ADR 0030: Microsoft.Z3 restores only from this pinned Z3Prover/z3 GitHub release nupkg, verified
# against the checked-in SHA-256, never from nuget.org (which stops at 4.12.2). Mirrors
# tools/z3-feed/fetch.ps1, which does the same thing for a local dev box.
COPY tools/z3-feed/*.sha256 tools/z3-feed/
RUN set -eux; \
    pkg=Microsoft.Z3.5.1.0.nupkg; \
    url="https://github.com/Z3Prover/z3/releases/download/z3-5.1.0/$pkg"; \
    expected=$(tr -d '\r\n' < "tools/z3-feed/$pkg.sha256"); \
    mkdir -p .z3-feed; \
    curl -fsSL -o ".z3-feed/$pkg" "$url"; \
    actual=$(sha256sum ".z3-feed/$pkg" | cut -d' ' -f1); \
    [ "$actual" = "$expected" ] || { echo "SHA-256 mismatch for $pkg: expected $expected, got $actual" >&2; exit 1; }

COPY global.json Directory.Build.props Directory.Packages.props NuGet.config .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Equiv.Cli/Equiv.Cli.csproj --locked-mode

# MinVer reads the version from a git checkout (MINVER1001 otherwise, falling back to
# 0.0.0-alpha.0), which this build stage does not have (.dockerignore excludes .git, same as any
# other image). release.yml passes the tag's version explicitly as VERSION; MinVerVersionOverride
# is MinVer's own escape hatch (plain -p:Version is overwritten by MinVer's own target). An ad hoc
# `docker build` with no build-arg keeps MinVer's untagged fallback.
ARG VERSION=""

# No -r/RuntimeIdentifier here: this is a framework-dependent publish, run by the final stage's own
# `dotnet`, not the self-contained single-file binary M3-004 also ships from `dotnet publish -r
# <rid>` (which needs IncludeAllContentForSelfExtract; see Equiv.Cli.csproj). The SDK base already
# carries the runtime, so bundling a second copy of it here would be wasted image size for nothing.
RUN dotnet publish src/Equiv.Cli/Equiv.Cli.csproj -c Release --no-restore -o /app ${VERSION:+-p:MinVerVersionOverride=$VERSION}

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS final
WORKDIR /app
COPY --from=build /app .
# ADR 0017: every release artifact carries the licence and third-party notices. BUSL applies
# separately per version (ADR 0017), so release.yml passes CHANGE_DATE (the release's own,
# four years out) as a build-arg; the checked-in LICENSE keeps its placeholder date otherwise.
COPY LICENSE THIRD-PARTY-NOTICES.md ./
ARG CHANGE_DATE=""
RUN if [ -n "$CHANGE_DATE" ]; then \
        sed -i -E "s/(Change Date:[[:space:]]+)[0-9]{4}-[0-9]{2}-[0-9]{2}/\1$CHANGE_DATE/" LICENSE; \
    fi

# action.yml overrides ENTRYPOINT with this for the GitHub Action's own invocation only; a plain
# `docker run equiv ...` (criterion 2) is unaffected and still runs the CLI directly (below).
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# M3-029: net4x reference assemblies are fetched into this path on first use, not baked into the
# image (about 110 MB per framework version). Mount a volume here to cache them across `docker run`
# invocations; README documents this.
ENV EQUIV_REFERENCE_ASSEMBLIES=/data/reference-assemblies
VOLUME ["/data/reference-assemblies"]

ENTRYPOINT ["dotnet", "/app/Equiv.Cli.dll"]
