# P2-051 `runtime-diff` measures Windows Forms and System.Drawing members, and every external callee
Status: done (PR #291)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-033

## Goal
Congruence is 91% of every Equivalent verdict on Git Extensions. It is only sound for runtime
behaviour that `runtime-changes.json` records: one `runtime-diff` pass over three `Path`/`StreamReader`
members turned 112 silent Equivalents into changed pairs (`docs/runs/2026-09-26-runtime-diff`).
That pass reached only 98 members. 127 of the 131 top Git Extensions callees it could not resolve
are `System.Windows.Forms.*` or `System.Drawing.*`, because `DriverFactory` references the BCL only,
and it tried only the top 200 callees per side. Extend the driver's references to the Windows
Desktop assemblies on both runtimes, generate the `System.Drawing` value types that blocked 12
overloads, and run the tool over every external callee of the four corpus pairs.

## Spec references
ADR 0035 decision 1; ticket M3-032 (DriverFactory's BCL-only scope); M3-033 (`externalCallees`,
measured rows); `tools/runtime-diff/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. ADR 0035 gains a dated Clarification: the measured "runtime" includes the Windows Desktop
   assemblies that .NET Framework 4.8 ships (`System.Windows.Forms`, `System.Drawing`) and their
   .NET 10 `Microsoft.WindowsDesktop.App` counterparts.
2. `DriverFactory` resolves a member of those assemblies on both runtimes. The .NET 10 driver targets
   `net10.0-windows`, and the 4.8 driver references the framework's own copies.
3. The M3-032 argument generators build `System.Drawing.Point`, `Size` and `Rectangle` (and their
   `F` variants) from generated integers or floats. A member whose parameter or receiver needs a
   live window handle or a message loop stays `not constructible`, with that as its reason.
4. `corpus.ps1 -RuntimeDiff` accepts `-Top all`. `docs/runs/<date>-runtime-diff/SUMMARY.md` is written
   for all four pairs in `docs/runs/2026-09-26-runtime-diff/SUMMARY.md`'s shape.
5. Every divergent member that no row covers becomes a `source: measured` row in
   `src/Equiv.Core/RuntimeChanges/runtime-changes.json` with its witness, and
   `docs/runtime-changes-review.md` gains it. The summary reports how many congruent pairs on Git
   Extensions lose congruence because of the new rows, as the 2026-09-26 run did.

## Files
`src/Equiv.Frontend.CSharp/Execution/DriverFactory.cs`, the M3-032 generator types,
`tools/runtime-diff/**`, `tools/corpus/corpus.ps1`, `docs/adr/0035-real-runtimes-are-a-second-oracle.md`
(Clarifications only), `src/Equiv.Core/RuntimeChanges/runtime-changes.json`,
`docs/runtime-changes-review.md`, `docs/runs/<date>-runtime-diff/SUMMARY.md`, tests.

## Tests
`DriverFactoryTests.ResolvesAWindowsFormsMemberOnBothRuntimes`,
`DriverFactoryTests.ResolvesASystemDrawingMemberOnBothRuntimes`,
`GeneratorTests.DrawingValueTypesAreGenerated`,
`DriverFactoryTests.WindowHandleParameter_IsNotConstructible`,
`RuntimeChangeTableTests.EveryRowHasASource` (existing, still green).

## Size guard
Running user code from the solution belongs to M4-009, not here. More than two new generator types
beyond the `System.Drawing` structs: stop.

## Out of scope
Generic-instantiation resolution (the `<T1,T2>` suffix; `tools/runtime-diff` README). WPF.
Third-party packages.

## Notes
- Decision: the ticket's `GeneratorTests.DrawingValueTypesAreGenerated` is `InputGeneratorTests.DrawingValueTypesAreGenerated`; the M3-032 generators are `Equiv.Execute.Inputs.InputGenerator`, whose tests already live there.
- Decision: the `System.Drawing` structs are four `ExecutionTypeKind` values (`Signed32Pair`, `Signed32Quad`, `Binary32Pair`, `Binary32Quad`), chosen by the type's name; the wire form is a JSON array of the components in constructor order. Edges are each component edge in every component, then `1, 2, ...`. A returned struct is canonicalised component-wise, so Windows Forms members returning `Size` are comparable.
- Decision: the .NET 10 driver gets the `Microsoft.WindowsDesktop.App` runtimeconfig (`net10.0-windows`) only when its compilation uses a Windows Desktop pack assembly (`GetUsedAssemblyReferences`); a BCL member's driver keeps `Microsoft.NETCore.App`, so it still runs where the desktop framework is absent. A desktop-pack file replaces the base pack's file of the same name (`System.Drawing.dll`, `WindowsBase.dll`, `Microsoft.VisualBasic.dll`), as SDK conflict resolution does.
- Decision: besides a window (`IWin32Window`, every `Control`) or `Message` receiver or parameter, members of `Application`, `Clipboard`, `Cursor`, `MessageBox` and `SendKeys` are not constructible with the same reason: generated inputs would show dialogs, send keystrokes or change the desktop session of the machine running the tool.
- Decision: `-Top` is a string validated as `all` or a positive integer; a member whose report already exists is skipped, so a multi-hour run resumes.
- Decision: `Environment::GetCommandLineArgs()` and `Marshal::GetLastWin32Error()` diverge only because of the harness (the driver's own file name; the start-up's residual last error) and get no row; SUMMARY.md says so. Two witnesses carry machine-specific values and are written with a placeholder the row's reason names.
- Decision: the congruence-loss census re-ran both sides (main's table, then with the new rows) from this branch, because lowering changed since 2026-09-26.
- Surprise: `externalCallees` spells `System.IntPtr` where .NET 10's symbols spell `nint`, so IntPtr members never resolve; harmless today, since no input can be built for IntPtr.
- Surprise: restoring `runtime-changes.json` with `Copy-Item` kept its older timestamp, so the incremental build silently re-embedded nothing and the first "after" census equalled "before". Touch the file before rebuilding.
- Decision: `tools/runtime-diff/Program.cs` now gives its host `.Within(work)`. The run's drivers had the caller's working directory, and with every external callee in scope, `File`/`Directory` members created about 80 files and folders with generated names in the repository root (untracked; not committed). `compare --execute` already did this (P2-040).
- Decision: no `Release:` footer. New runtime-changes rows change no surface equiv-release lists (CLI, exit codes, SARIF shape, verdict meanings, file formats); M3-033's measured rows shipped as a patch too.
