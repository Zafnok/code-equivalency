# P2-266 Precision bug: the `FieldInfo.SetValue` row fires on a field that is not `static readonly`
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
.NET Core 3.0 made `FieldInfo.SetValue` throw for a `static readonly` field after its type is initialized; it changed
nothing for any other field. The row fires on every call. P2-247 (`docs/runs/2026-10-20-precision-execute-verdict-2.md`,
appendix C) found two sampled exposures, both false positives:
- OpenRA `TerrainTemplateInfo::LoadTileInfo(TileSet,MiniYaml)`: every `SetValue` targets an instance field of a fresh
  `TerrainTileInfo` (through `FieldLoader.Load`);
- Git Extensions `U04` of appendix C: the `Cursors` fields it sets by name are plain statics on .NET Framework and are
  named otherwise on .NET 5, so `GetField` returns `null` there and the call is never made.
When done, the row fires only where the `FieldInfo` can be a `static readonly` field: not when it comes from
`GetField`/`GetFields` with `BindingFlags.Instance` and without `Static`, nor from a type known to declare no such field.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the `FieldInfo::SetValue` row), `RowPreconditions`,
`docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Unit tests: `SetValue` on a `FieldInfo` from `GetFields(BindingFlags.Instance | BindingFlags.Public)` fires no row;
   on one from `GetField(name, BindingFlags.Static | BindingFlags.NonPublic)` it still does.
2. `openra-17989` and `gitextensions-8522` in `full`: both results are given with their result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247.
