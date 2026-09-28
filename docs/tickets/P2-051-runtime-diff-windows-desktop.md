# P2-051 `runtime-diff` measures Windows Forms and System.Drawing members, and every external callee
Status: todo
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
