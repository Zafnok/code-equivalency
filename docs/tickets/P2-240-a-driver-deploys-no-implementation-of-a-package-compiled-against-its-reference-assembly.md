# P2-240 Execution bug: a driver deploys no implementation of a NuGet package its project compiled against a reference assembly
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-237

## Goal
`ProjectEmitter.Emit` copies each project's references beside the replay driver, except those in a folder that holds a
reference assembly, which it takes for a runtime pack the runtime supplies. A NuGet package that ships a `ref/` assembly
and its implementation elsewhere (`System.Management` 4.7.0: `ref/netstandard2.0` to compile, `runtimes/win/lib/...` to
run) is such a folder, so nothing of it is copied, and at run time the driver throws `FileNotFoundException` for it. P2-237
saw this on `duplicati-3124`'s `WindowsPowerSupplyState::GetSource()` under `probe`: legacy returned, modern threw
`FileNotFoundException`, a difference the solution itself would not show. A replay counts any such difference as
`reproduced` (ADR 0035 decision 2), so it can confirm a Divergent that is not real, and an Unknown's differential test can
make one `observed`.

When done, a driver runs each package's runtime implementation for the side's runtime, as the project's own build output
would, and a reference assembly is never mistaken for the absence of one.

## Spec references
ADR 0035 decisions 2 and 3, ADR 0040 decision 3, `src/Equiv.Frontend.CSharp/Execution/ProjectEmitter.cs`,
`ReplayDriverFactory`, `docs/runs/2026-10-17-precision-execute-verdict.md` (P2-240's row, and Duplicati's sample row 8).

## Acceptance criteria (all must hold; nothing beyond them)
1. A package reference whose compile asset is a reference assembly is deployed from the restore's runtime assets for the
   side's runtime (the project's `project.assets.json` names them; a `packages.config` project's `lib/<tfm>` folder is
   its implementation), and a shared-framework or targeting-pack reference is still not copied.
2. A package whose runtime asset cannot be found makes the plan not constructible with a reason naming the package, never
   a driver that runs without it.
3. Re-running `probe` on `duplicati-3124`'s `WindowsPowerSupplyState::GetSource()` no longer throws
   `FileNotFoundException` on the modern side (recorded in Notes, with what each side returns).
4. Every `reproduced` replay in P2-237's three runs is checked against the fix: rerun `gitextensions-8522` and
   `duplicati-3124` in `full --execute` (quick, `--jobs 4`), and list any result whose replay changes, in Notes.

## Tests
Unit tests over a fake restore layout: a package with `ref/` and `runtimes/win/lib/` assets is deployed from the runtime
asset; a targeting-pack reference is not copied; a missing runtime asset is not constructible with its reason. An
integration test against a `samples/` project that references a package of that shape.

## Size guard
If deploying runtime assets needs a full `dotnet publish` of the project, stop and record why: the driver must stay
buildable from the loaded compilation.

## Out of scope
Native assets (`runtimes/<rid>/native`), unless the same change carries them. The `probe` config (P2-241). Constructors
and structs (P2-242).

## Notes
- Filed 2026-10-17 by P2-237 (`docs/runs/2026-10-17-precision-execute-verdict.md`).
- Decision: a project's restore file is found from what MSBuildWorkspace loads, without evaluating MSBuild again: in
  the four folders above its intermediate assembly (`Project.CompilationOutputInfo.AssemblyPath`, `obj/<config>/<tfm>[/<rid>]`,
  which sits under `MSBuildProjectExtensionsPath` by default), else `<project dir>/obj/project.assets.json`. It is looked
  for when a replay first needs it, so a run without `--execute` reads nothing.
- Decision: the target is the restore's target without a runtime identifier whose compile assets the compilation
  references most of, so a multi-targeted project's compilation gets its own framework's target.
- Decision: a package's runtime assets are chosen as the host picks them from a `deps.json`: the `runtimeTargets` of the
  most specific runtime identifier in the portable graph of this machine's (`RuntimeInformation.RuntimeIdentifier`;
  `win-x64`, `win`, `any`), else its plain `runtime` assets. A driver has no `deps.json`, so the chosen files go beside it.
- Decision: a package that a referenced project compiles against is resolved by the deployed project's restore first (it
  unified the version, as the deployed project's build output does), else by the referenced project's own.
- Decision: only packages a compilation references a compile asset of are deployed, in place of that asset; a
  runtime-only transitive package is not, and native assets are not carried (Out of scope).
- Decision: a package whose runtime asset is NuGet's `_._` placeholder deploys nothing and is not an error: the runtime
  supplies it, as in the build output. A runtime asset the restore names that is not on disk is the error (criterion 2).
- Decision: a reference assembly in a `<id>/<version>/ref/<tfm>` folder that has a `.nuspec`, and no
  `data/FrameworkList.xml` (a targeting pack restored as a package has one), and that no restore names, is the error
  too, so a project whose restore file is not found never gets a driver without the package. An unreadable restore file
  counts as absent. The bare loader's non-SDK projects (Linux) are not in the loaded `Solution`, so they have no restore
  file: a package they compile against through a reference assembly is now not constructible instead of
  `FileNotFoundException` at run time.
- Decision: the error reads `emit-failed: <side> project <assembly>: the package <id> <version> runs <path>, which is not on
  disk` (or `... is compiled against its reference assembly, and no restore names its runtime asset`), through the
  emitter's existing error path.
- Decision: the integration test is `probe` on a new sample, `samples/package-runtime-asset` (`System.Management` 4.7.0 on
  the modern side, the framework's on the legacy side): ADR 0002 and THIRD-PARTY-NOTICES gain its samples-only row. With
  the build before this change the modern side threw `FileNotFoundException` there; with it both return the query text.
