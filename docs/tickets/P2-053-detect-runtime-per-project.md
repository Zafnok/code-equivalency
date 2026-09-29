# P2-053 Detect each project's runtime and report it
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none (ADR 0040, accepted 2026-09-28)

## Goal
Nothing in `src/` knows which runtime a side runs on. The bare loader reads `TargetFrameworkIdentifier`
for its own use, and nothing reaches analysis. Give every loaded project a runtime, resolve
`netstandard` projects to their hosts, let `equiv.json` state a runtime where there is no host, and
report the result. Nothing consumes it yet; P2-055 and P2-056 do.

## Spec references
ADR 0040 decision 1; P2-016 (`LastFlavour` for multi-targeted projects); VERIFICATION-MODEL.md run properties.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Equiv.Core` has a `TargetRuntime` record, `(Family, Version)`, with Family `NetFramework` or
   `NetCore`. It has a total order in which every `NetFramework` version precedes every `NetCore`
   version. It parses `.NETFramework,Version=v4.8` and `.NETCoreApp,Version=v8.0`, and
   `net48` / `net8.0`.
2. The frontend reads each project compilation's `System.Runtime.Versioning.TargetFrameworkAttribute`
   through Roslyn's attribute API (no reflection), for both loaders. A multi-targeted project uses the
   flavour `LastFlavour` already picks.
3. A `.NETStandard` project's runtime is the set of runtimes of the executable and test projects on
   the same side that reference it, directly or transitively.
   - With no such host, it takes `runtimes.legacy` or `runtimes.modern` from `equiv.json`, if set.
   - Otherwise it is `unhosted`, keeping its `netstandard` version.
4. `equiv.json` accepts `"runtimes": { "legacy": "<tfm>", "modern": "<tfm>" }`, and the loader
   validates it with a diagnostic, as it does the other keys.
5. SARIF `run.properties.runtimes` is `{ legacy: [...], modern: [...] }`, with one entry per loaded
   project: `{ project, runtime, source }`. `source` is `attribute`, `host`, `config` or `unhosted`.
6. VERIFICATION-MODEL.md documents `runtimes` and the `runtimes` config key, citing ADR 0040.

## Files
`src/Equiv.Core/TargetRuntime.cs` (new), `src/Equiv.Core/Configuration/EquivConfig.cs`,
`src/Equiv.Core/Configuration/EquivConfigLoader.cs`, the frontend's loaded-project model
(`src/Equiv.Frontend.CSharp/Loading/LoadedSolution.cs`), `src/Equiv.Frontend.CSharp/CSharpFrontend.cs`, `src/Equiv.Core/Reporting/SarifReportWriter.cs`,
`docs/VERIFICATION-MODEL.md`, tests, snapshots.

## Tests
`TargetRuntimeTests.FrameworkPrecedesCore`, `TargetRuntimeTests.ParsesMonikersAndShortNames`,
`RuntimeDetectionTests.ReadsTheTargetFrameworkAttribute`,
`RuntimeDetectionTests.NetStandardTakesItsHostsRuntimes`,
`RuntimeDetectionTests.UnhostedNetStandardUsesConfig`, `RuntimeDetectionTests.UnhostedWithoutConfig`,
`EquivConfigLoaderTests.Runtimes_AreValidated`, and snapshot updates for `run.properties.runtimes`.

## Size guard
Any change to lowering, encoding or verdicts belongs to P2-055. More than one new Core type: stop.

## Out of scope
Using the runtime (P2-054 to P2-056). Loading non-SDK projects that target .NET Core.

## Notes
- Decision: `TargetRuntime.RuntimeFamily` is an enum nested in `TargetRuntime`, so Core gains one type. `TargetRuntime.Parse` returns null for anything that is neither .NET Framework nor .NET (Core), `netstandard` included.
- Decision: the per-project result is frontend-internal (`Loading/ProjectRuntime.cs`, `Loading/RuntimeDetection.cs`, held on `LoadedSolution.Runtimes`); Core's `FrontendAnalysis` carries only the reported `(Project, Runtime, Source)` strings, since P2-055 and P2-056 consume the runtime inside the frontend.
- Decision: `project` is the assembly name, the key P2-016's `LastFlavour` and ADR 0029's skipped-project matching already use.
- Decision: a hosted project whose hosts run on several runtimes is one entry, `runtime` listing them in runtime order joined by `, ` (criterion 5 says one entry per project).
- Decision: a test project is one that references `xunit.core`, `xunit.v3.core`, `nunit.framework` or `Microsoft.VisualStudio.TestPlatform.TestFramework`; an executable is `OutputKind` Console, Windows or WindowsRuntime application. A host without a readable runtime hosts nothing.
- Decision: a project with no `TargetFrameworkAttribute`, or one naming neither family (`.NETPortable`), is resolved like `netstandard`; unhosted it reports its moniker as written, or `unknown`.
- Decision: the config file is `equiv.config.json` (the ticket's `equiv.json`); the new diagnostic is `CFG009`.
- Decision: the SARIF property is built in `CompareCommand.RunProperties` beside `projectsNotBuilt`, with the list shape in `SarifReportWriter.RuntimesProperty`.
