# P2-257 Execution reach: a replay builds a framework struct the model holds as a free sort element
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-251

## Goal
The C# lowering maps a framework struct such as `System.DateTime` or `System.Drawing.Color` to the sort of its metadata
name. Replay builds a struct outside the solution only from public fields it can set (`DriverStruct`), and these have
none, so a replay whose model holds one is `not-constructible` ("no `System.DateTime` argument can be built for
`lastWrite` from the model") even when the model gives the element no field, no cast and no type test. Two such replays
in P2-251's runs: `Duplicati.Library.Compression.FileArchiveDirectory::CreateFile(string,Duplicati.Library.Interface.CompressionHint,System.DateTime)`
on `duplicati-3124` (its enum `hint` is built since P2-251) and
`GitExtensions.Plugins.GitStatistics.PieChart.PieSlice::CreateBrushForSide(System.Drawing.Color,double)` on
`gitextensions-8522`. When done, a framework struct's element that no map of the model constrains is built as a value
of that type, distinct elements as distinct values (by a public constructor or factory the type has, or say why not),
so that equal elements are equal values; an element the model does constrain stays `not-constructible`, with its reason.

## Spec references
VERIFICATION-MODEL section 6 (the replay paragraph), ADR 0062 decision 3, `src/Equiv.Frontend.CSharp/Execution/ModelObjects.cs`
(`Struct`), `DriverStruct`, P2-251 (`EnumElements`, the distinct-per-element ranking).

## Acceptance criteria (all must hold; nothing beyond them)
1. A replay builds a framework struct argument or field the model leaves free as the Goal says; a unit test covers one
   element, two distinct elements of one sort, and an element the model constrains.
2. The two replays above are rerun and each one's replay outcome, or the reason it does not run, is given.

## Notes
- Filed 2026-10-19 by P2-251 (the next `not-constructible` reason behind the enum ones it removed).
