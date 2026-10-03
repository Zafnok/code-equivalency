<div align="center">

# equiv

**Behavioural equivalence checker for migrated code.**<br>
It proves that before and after a migration behave the same, or shows you the input where they don't.

[![CI](https://github.com/Zafnok/code-equivalency/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Zafnok/code-equivalency/actions/workflows/ci.yml)
[![CodeQL](https://github.com/Zafnok/code-equivalency/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/Zafnok/code-equivalency/actions/workflows/codeql.yml)
[![Mutation testing](https://github.com/Zafnok/code-equivalency/actions/workflows/mutation.yml/badge.svg?branch=main)](https://github.com/Zafnok/code-equivalency/actions/workflows/mutation.yml)
[![SonarQube Cloud](https://github.com/Zafnok/code-equivalency/actions/workflows/sonar.yml/badge.svg?branch=main)](https://github.com/Zafnok/code-equivalency/actions/workflows/sonar.yml)

[![Quality gate status](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=coverage)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Maintainability rating](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Reliability rating](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Security rating](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=bugs)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Code smells](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)
[![Lines of code](https://sonarcloud.io/api/project_badges/measure?project=Zafnok_code-equivalency&metric=ncloc)](https://sonarcloud.io/summary/new_code?id=Zafnok_code-equivalency)

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](global.json)
[![C# 14](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)](Directory.Build.props)
[![Z3](https://img.shields.io/badge/solver-Z3-2F6DB5)](docs/adr/)
[![SARIF 2.1.0](https://img.shields.io/badge/output-SARIF%202.1.0-orange)](docs/VERIFICATION-MODEL.md)
[![Coverage gate](https://img.shields.io/badge/coverage%20gate-100%25%20line%20%2B%20branch-brightgreen)](docs/QUALITY-GATES.md)
[![Warnings as errors](https://img.shields.io/badge/warnings-as%20errors-critical)](docs/QUALITY-GATES.md)
[![License: BSL 1.1](https://img.shields.io/badge/license-BSL%201.1-blue)](LICENSE)

[![Last commit](https://img.shields.io/github/last-commit/Zafnok/code-equivalency?logo=github)](https://github.com/Zafnok/code-equivalency/commits/main)
[![Commit activity](https://img.shields.io/github/commit-activity/m/Zafnok/code-equivalency)](https://github.com/Zafnok/code-equivalency/pulse)
[![Merged PRs](https://img.shields.io/github/issues-pr-closed/Zafnok/code-equivalency?label=PRs%20closed)](https://github.com/Zafnok/code-equivalency/pulls?q=is%3Apr+is%3Aclosed)
[![Open issues](https://img.shields.io/github/issues/Zafnok/code-equivalency)](https://github.com/Zafnok/code-equivalency/issues)
[![Top language](https://img.shields.io/github/languages/top/Zafnok/code-equivalency)](https://github.com/Zafnok/code-equivalency)
[![Repo size](https://img.shields.io/github/repo-size/Zafnok/code-equivalency)](https://github.com/Zafnok/code-equivalency)
[![Stars](https://img.shields.io/github/stars/Zafnok/code-equivalency?style=social)](https://github.com/Zafnok/code-equivalency/stargazers)

<img src="docs/assets/demo.svg" alt="Terminal session: equiv compare exits 1 on removed-null-check with an EQ002 counterexample where name is null, then exits 0 on renamed-locals with all three pairs Equivalent" width="880">

<sub>Replayed from real <code>equiv compare</code> output at <code>cc56ae8</code> on two <code>samples/</code>; the SARIF message is wrapped and the comments are added.</sub>

</div>

`equiv` proves (or refutes, with a counterexample) that a program behaves the same
before and after a migration. It does not diff syntax. It lowers both versions to a
language-neutral intermediate representation (IR), encodes matched procedure pairs as
SMT problems, and asks Z3 whether any input can make them disagree.

Scope: **any two C# solutions on .NET Framework 4.x or .NET 3.0 and later**, whether a migration,
a version upgrade or a same-runtime change. Each side's runtime is read from its projects.
Java 11 → 25 and cross-language rewrites are planned.

## How it works

```mermaid
flowchart LR
    L["legacy<br/>the solution before the change"] --> F["Equiv.Frontend.CSharp<br/>Roslyn CFG"]
    M["modern<br/>the solution after it"] --> F
    F --> Match["match procedures<br/>(identity, rename map, HTTP route)"]
    Match --> IR["Equiv.Core<br/>SSA IR"]
    IR --> Z3["Equiv.Verify.Z3<br/>product program → SMT<br/>loop ladder, rungs 1–5"]
    Z3 --> S["SARIF 2.1.0<br/>Equivalent · Divergent + counterexample<br/>Unknown · Added · Removed"]
    Z3 -. "--execute" .-> X["Equiv.Execute<br/>replay and differential testing<br/>on both real runtimes"]
    X -.-> S
```

## What it does

- **Loads both solutions** (.NET Framework and .NET, SDK-style and legacy `.csproj`) on Windows
  or Linux.
- **Matches procedures** across the two sides by identity, by a rename map you supply in
  `equiv.config.json`, or by HTTP route (Web API 2, MVC 5 and ASP.NET Core attribute routes).
- **Proves or refutes each matched pair** with Z3. Loops go through a five-step ladder: bounded
  unrolling, lockstep relational induction, k-induction, Spacer CHC, then Z3-checked loop
  invariants.
- **Reports one verdict per pair** in SARIF 2.1.0: `Equivalent`, `Divergent` with a concrete
  counterexample, `Unknown` with a reason and a scope, `Added` or `Removed`. Known behaviour
  changes between runtimes are reported as their own rule (EQ006).
- **Ranks what to review**, grouping the Divergent and Unknown results by cause, most certain
  first.
- **Supports baselines**, so CI fails only on new findings.
- **Optionally runs the code** (`--execute`): it replays every counterexample on both real
  runtimes and tests every Unknown pair on generated inputs. Running the code never proves a pair
  Equivalent.

Which C# constructs are supported is listed in
[docs/tickets/IOPERATION-COVERAGE.md](docs/tickets/IOPERATION-COVERAGE.md). The
[`samples/`](samples/) folder holds small paired solutions, and each one's README states the
verdicts it should produce.

## Licence

`equiv` is **source-available, not open source**. It is licensed under the
[Business Source License 1.1](LICENSE); each version converts to Apache-2.0 on the Change
Date (2030-09-20) or four years after that version is first published, whichever is sooner.

In plain terms, production use is free while **both** of the following hold:

- no more than **3 people** use `equiv` or act on its output, and
- **no codebase you analyse exceeds 50,000 lines** (each side of a comparison is measured
  separately, so 49k against 49k is fine and 49k against 52k is not).

Using `equiv` in CI counts as production use. Non-production evaluation is free and uncapped.

**How `equiv` counts lines for the 50,000 limit.** Every `equiv compare` prints the analysed
line count of each codebase, and writes it to the SARIF run's `properties.analysedLinesOfCode`
as `legacy` and `modern`. They are two numbers, never a total, because the licence measures each
codebase on its own. The same rule applies to both sides:

- **Files:** every C# file the loaded projects compile, which is your source files plus what the
  build generates (such as `obj/**/AssemblyInfo.cs` and source-generator output). A file that
  more than one project compiles counts once.
- **Lines:** a line counts when it holds C# code. Blank lines, comment-only lines, preprocessor
  directive lines (`#if`, `#region`) and code that an inactive `#if` excludes do not count.

`equiv compare --dry-run` reports the two counts without verifying anything, so you can check
where you stand first. `equiv` only reports the numbers. It enforces nothing and sends nothing
anywhere.
Offering `equiv` as a hosted or embedded service, using it to run migrations or reviews for
third parties, or reselling it, is never permitted under this licence at any size.

[LICENSE](LICENSE) is the controlling text and this summary is not a substitute for it. For a
commercial licence, open an issue. Reasoning and the dependency licence policy are in
[ADR 0017](docs/adr/0017-licensing-and-ip.md); attribution for bundled third-party code is in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Usage

```
equiv compare --legacy <solution.sln|.slnx> --modern <solution.sln|.slnx>
              [--out equiv.sarif] [--baseline <previous.sarif>]
              [--config equiv.config.json] [--fail-on divergent|unknown]
              [--dry-run] [--lower-only]
              [--execute [--test-target 0.001] [--test-budget <inputs>[,<seconds>]]]
              [--chc-int-mode true|false] [--invariant-model <id>] [--il-fallback]
              [--resource-limit <n>]
              [--verbosity quiet|normal|debug] [--log <path>]
equiv mcp [--execute]
```

`--legacy` is the solution before the change and `--modern` the solution after it; `--before` and
`--after` are aliases, and giving both spellings of one option is exit 3.
Both paths must be solution files (`.sln` or `.slnx`); anything else is exit 3. Stdout carries
`analysed lines of code: legacy=<n> modern=<n>`, a `route:` line with `--dry-run`, and after a run
that verifies, the review list: `review list: <G> groups for <R> flagged results` and its ten
highest-ranked groups, one line each (the Divergent and Unknown results grouped by cause, most
certain first; `docs/VERIFICATION-MODEL.md` section 6). Results go only to the SARIF file, which
holds the whole list in `run.properties.reviewList`.

`--dry-run` routes and loads both sides, prints the analysed line counts, and stops without
writing SARIF. `--lower-only` loads, matches and lowers, then writes a SARIF log with the
lowering census (`run.properties.loweringCensus`) and the Added and Removed results. It never
runs the solver and exits 0 unless loading fails. It cannot be combined with `--baseline` or
`--fail-on`.

`--execute` runs code from both solutions on this machine, each side on the runtime its projects
target, which must be installed. It needs Windows only when a side targets .NET Framework
(otherwise exit 3). It replays every Divergent's counterexample on both runtimes and records
the outcome in `properties.replay`: `reproduced`, `not-reproduced`, `not-applicable` or
`not-constructible`. It also tests every Unknown pair on generated inputs, and a divergence it
sees twice becomes an EQ002 with `proofMethod: observed`. Testing a pair stops once the estimated
chance of new behaviour drops below `--test-target` (default 0.001, after at least 1,000 inputs),
or at `--test-budget` (default `10000,60`: 10,000 inputs or 60 seconds). Execution never yields
Equivalent. See [VERIFICATION-MODEL.md](docs/VERIFICATION-MODEL.md) for what each field means.

`--invariant-model <id>` lets the loop ladder ask the Claude model `<id>` for a loop invariant
when Z3 Spacer times out (rung 5). It sends the loops' IR text and needs `ANTHROPIC_API_KEY`.
Z3 checks every answer, so a wrong one can never prove a pair. It is off by default. The local
proposer, which mines invariants from runs in process and sends nothing, is on by default and
is asked first. `--chc-int-mode` (default `true`) lets rung 4 try integer arithmetic before
falling back to bitvectors.

`--resource-limit <n>` overrides the config's `resourceLimit` (default 5,000,000), the budget of each
solver query in Z3's own step count. It is deterministic, so a pair that runs out of it is
Unknown (`timeout`) on every run, whatever the machine's speed or load. The config's `timeoutMs`
(default 60000) is the wall-clock backstop behind it. The Unknown's message says which of the two
was hit.

`--il-fallback` (off by default) lowers a matched pair again from IL on both sides when
it is not congruent and either side holds an opaque the other lacks, and keeps the IL bodies only
when they hold fewer such opaques. Every result on a matched pair then says which lowering it used,
in `properties.lowering` (`operation` or `il`), and the census counts `pairsIlFallbackTried` and
`pairsLoweredFromIl`.

Progress goes to stderr, never stdout. `normal` prints each phase's start and end, a
line at most every 5%, and a heartbeat every 60 s that names the pair being worked on. `debug`
adds one line per item, the solver's rung timings, and each pair's time by stage (encoding,
inlining, every solver query with its answer, disposal). `quiet` prints nothing. `--log <path>`
copies the same lines to a file that is flushed line by line. The grammar is fixed, so scripts
can parse it:

```
equiv: +00:00:06 verify 1/1 (100%) eta=00:00:00.000 worst=00:00:00.000 rate=8.0/s
equiv: +00:00:06 verify done in 00:00:00.124; eta@25%=00:00:00.000 eta@50%=00:00:00.000 eta@75%=00:00:00.000 dropped=0
```

| Exit code | Meaning |
|---|---|
| 0 | No new `Divergent` results (and no new `Unknown` when `--fail-on unknown`) |
| 1 | At least one `Divergent` result that is `new` relative to `--baseline` |
| 2 | `--fail-on unknown` and at least one new `Unknown` result |
| 3 | Usage error: bad arguments, missing or unparseable file, no frontend for the paths |
| 4 | A C# project was skipped because it failed to load (the SARIF lists it and is still written), or a side had no C# project that loaded (no SARIF). Outranks 1 and 2. A modern project that loads but does not compile is not skipped: its methods that do not bind are `Unknown` results with reason `unbound`, the rest are compared, and the exit code is 0, 1 or 2 as the verdicts say |
| 5 | At least one pair's lowering or verification crashed (the SARIF lists it as a tool-execution notification and is still written), or any other unhandled internal error. Outranks 1, 2 and 4 |

Output is always SARIF 2.1.0 (rules EQ001 to EQ006); the exact meaning of each verdict
and of `baselineState` is in [docs/VERIFICATION-MODEL.md](docs/VERIFICATION-MODEL.md).

A real `Divergent` result, from `equiv compare --legacy samples/added-branch/legacy/*.sln
--modern samples/added-branch/modern/*.slnx` (see `samples/added-branch/README.md`),
one result from `equiv.sarif`'s `runs[0].results`:

```json
{
  "ruleId": "EQ002",
  "level": "error",
  "message": {
    "text": "Equiv.Samples.AddedBranch.Doubler::Double(int) diverges: inputs(bv32 0) old(returned bv32 0 outs() trace()) new(returned bv32 4294967295 outs() trace())"
  },
  "properties": {
    "model": "inputs(bv32 0) old(returned bv32 0 outs() trace()) new(returned bv32 4294967295 outs() trace())",
    "ladderTrace": [
      { "rung": "bounded", "outcome": "refuted", "detail": "a divergence within 3 iterations" }
    ]
  }
}
```

The counterexample is `x = 0` (`bv32 0`): legacy returns `0`, modern returns `-1` (`bv32
4294967295` two's-complement). The exit code is 1.

An `Unknown` (EQ003) says how far it can be trusted. `properties.scope` is `line` when the pair
is equivalent unless one of the listed `relatedLocations` is reached, and `method` otherwise.
`properties.failureRefinement` says whether the solver found, or ruled out, an input on which the
modern side throws where the legacy side returns (`newFailures`), and the reverse
(`removedFailures`), each as `found`, `none-proved` or `unknown`.

## Use from a coding agent

`equiv mcp` runs the same pipeline as `equiv compare` as a [Model Context Protocol](https://modelcontextprotocol.io)
server over stdio, so a coding agent can ask "is my port equivalent?" while it works. It
has two read-only tools that write no file:

- `compare`: `legacy` and `modern` (solution paths, required), and optionally `config`, `baseline`,
  `bound`, `timeoutMs` and `ilFallback` (`--il-fallback`). The result is a short summary (`Equivalent n, Divergent n, Unknown n,
  skipped projects n, exit code k`, then the review list's lines), then the SARIF log `equiv compare` would write, as JSON text.
- `lower_only`: `legacy`, `modern` and optionally `config` and `ilFallback`; the same as `equiv compare --lower-only`.

An input error `equiv compare` exits 3 or 4 on (a missing file, no frontend for the paths, no C#
project that loads) comes back as a tool error with the same message.

`equiv mcp --execute` also registers `probe`, which runs code
from both solutions on this machine and so needs Windows for a .NET Framework side, same as
`compare --execute`: an agent that
gets Unknown back from `compare` can name a matched pair by its normalised identity and supply its
own arguments, and get back both runtimes' outcomes to test its own hunch. `probe` never writes
SARIF and never changes a `compare` result; a mismatch it reports is a hypothesis, not a proof.
Without `--execute`, `probe` is not registered at all, so an agent cannot turn execution on by
itself.

- `probe`: `legacy`, `modern`, `identity` and `arguments` (the method's own arguments, in order, as
  JSON values), and optionally `culture`. The result is `{ legacy: {kind, canonical}, modern: {kind,
  canonical}, equal }`. An argument that cannot be built, or an identity that matches no pair, comes
  back as a tool error naming it.

Every MCP host takes a stdio server as a command and its arguments; for the binary:

```json
{ "mcpServers": { "equiv": { "command": "equiv", "args": ["mcp"] } } }
```

and for the container, with the repository mounted so the paths you pass resolve inside it:

```
docker run -i --rm -v <repo>:/src equiv mcp
```

Only protocol messages go to stdout; the run's own progress and messages go to stderr.

## Installing

Each release has three ways to run `equiv` without a checkout. Every commit on `main` that passes
CI is published as a patch release. Every artifact carries `LICENSE` and `THIRD-PARTY-NOTICES.md`.
The licence is BUSL-1.1 (source-available, not open source), and each release has its own Change
Date.

**Single-file binary.** Download `equiv-<version>-win-x64.zip` or `equiv-<version>-linux-x64.tar.gz`
from the GitHub release, extract it and run `equiv compare ...` directly. The
[requirements](#requirements) below still apply.

**Container.**

```bash
docker run --rm --user "$(id -u):$(id -g)" -v <dir>:/samples ghcr.io/zafnok/equiv:<version> compare --legacy /samples/legacy/<name>.sln --modern /samples/modern/<name>.slnx --out /samples/equiv.sarif
```

Mount the directory that holds the solutions read-write, because loading writes `obj/` there.
The image runs as a non-root user (uid/gid 1654), so `--user "$(id -u):$(id -g)"` makes its
writes into your mounted directory land with your own ownership instead of failing with
"Permission denied". The image is based on the .NET SDK, because SDK-style projects are evaluated
by the SDK's MSBuild, and is about 1.6 GB. It holds no .NET Framework reference assemblies or
NuGet packages. Those are fetched into `$EQUIV_REFERENCE_ASSEMBLIES`
(`/data/reference-assemblies`) on first use, so mount a volume there
(`-v equiv-ref-assemblies:/data/reference-assemblies`) to avoid fetching them on every run.

**GitHub Action.** `action.yml` at the repo root runs the container with inputs `legacy`,
`modern`, `config`, `baseline` and `fail-on`, and output `sarif`. Follow it with
`github/codeql-action/upload-sarif` to get the results into Code Scanning:

```yaml
- uses: zafnok/code-equivalency@v0.1.0
  id: equiv
  with:
    legacy: legacy
    modern: modern
- uses: github/codeql-action/upload-sarif@v4
  with:
    sarif_file: ${{ steps.equiv.outputs.sarif }}
    category: equiv
```

SonarQube can also read the same SARIF file through `sonar.sarifReportPaths`.

## Requirements

### Windows

- The .NET 10 SDK.
- Visual Studio 2026 **Build Tools** with the workload ".NET desktop build tools" and the
  component ".NET Framework 4.8 targeting pack". This is what lets `equiv` load legacy
  (non-SDK) `.csproj` files. Build Tools install under
  `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools` (the x86 prefix, even on 64-bit
  Windows), and the net48 reference assemblies land under
  `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`.
  Unattended install with the exact component ids:

  ```bash
  winget install --source winget Microsoft.VisualStudio.BuildTools --override "--wait --quiet --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.Net.Component.4.8.TargetingPack --add Microsoft.Net.Component.4.8.SDK"
  ```

  Without the targeting pack, legacy projects fail to load with `CS0518` (predefined type
  not defined), not with a workspace error.

### Linux

Off Windows, `equiv` loads legacy (non-SDK) `.csproj` files with its own loader, and SDK-style
ones through MSBuild on the .NET SDK. A Linux host needs:

- the **.NET 10 SDK**, not only the runtime: SDK-style projects are evaluated by its MSBuild.
- **glibc 2.38 or newer** (Ubuntu 24.04+), which the bundled Z3 native library is built against.
  Debian 12, Ubuntu 22.04 and Alpine are not supported.
- **network access to the package sources** the solution's `nuget.config` names (nuget.org when
  it names none), or a pre-filled cache. `equiv` restores `packages.config` packages into the
  folder their HintPaths expect, and on first use fetches the .NET Framework reference assemblies
  (`Microsoft.NETFramework.ReferenceAssemblies.<tfm>`, about 110 MB per framework version) into
  `$EQUIV_REFERENCE_ASSEMBLIES`, by default `~/.local/share/equiv/reference-assemblies`. A
  version already in that folder (layout `.NETFramework/v4.8/...`) is never fetched again.
- a **`dotnet restore`** of any legacy project that uses `<PackageReference>`: the loader reads
  the `project.assets.json` it writes, exactly as Visual Studio's `ResolveNuGetPackageAssets`
  does.

No Mono, nuget.exe or MSBuild.exe is needed. A legacy project that uses MSBuild the loader cannot
evaluate exactly (`<Choose>`, a property function in a property it reads, a target that adds
`Compile` or `Reference` items, a COM reference, ...) is skipped with a notification naming the
construct (exit 4), never loaded approximately.

CI fails any change for which `equiv compare` gives different SARIF results on Windows and Linux
for any sample.

## Building from source

Requires the .NET 10 SDK (the exact patch is pinned in `global.json`) and, on Windows, the Build
Tools above.

```
./build.ps1               # build, format check, tests, 100% coverage gate, architecture tests
./build.ps1 -Integration  # also runs tests/Equiv.Tests.Integration (needs VS Build Tools)
```

`Microsoft.Z3` restores only from the git-ignored local feed `.z3-feed/`, which `build.ps1`
fills first. Before any direct `dotnet restore`, `dotnet build` or `dotnet test` on a fresh clone
(an IDE build included), run `./tools/z3-feed/fetch.ps1` once. `./build.ps1 -Integration` loads
every sample and is the quickest check that a Windows box is set up.

```
src/        production code, one project per component (see docs/ARCHITECTURE.md)
tests/      one test project per src project, plus architecture and integration tests
samples/    small paired legacy/modern solutions used as fixtures and demos
tools/      build and gate tooling
docs/       architecture, verification model, quality gates, decision records
```

## Further reading

- [docs/VERIFICATION-MODEL.md](docs/VERIFICATION-MODEL.md): what "equivalent" means here, exactly.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): components, boundaries and data flow.
- [docs/QUALITY-GATES.md](docs/QUALITY-GATES.md): the gates every change must pass.
- [docs/adr/](docs/adr/): why each technology was chosen, and what was rejected.

<!--
Star history chart, hidden until the repo has stars. With 0 stars star-history.com has no data
points, so its time axis collapses to a single instant and renders a sub-second tick (".473")
instead of dates. Restore this section once the Stars badge above shows 1 or more.

## Star history

<a href="https://star-history.com/#Zafnok/code-equivalency&Date">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=Zafnok/code-equivalency&type=Date&theme=dark">
    <img alt="Star history chart" src="https://api.star-history.com/svg?repos=Zafnok/code-equivalency&type=Date">
  </picture>
</a>
-->

