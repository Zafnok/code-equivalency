# P2-240 Execution bug: a driver deploys no implementation of a NuGet package its project compiled against a reference assembly
Status: todo
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
