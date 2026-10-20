# P2-273 Execution reach: a replay or `probe` driver builds a generic collection argument (`IEnumerable<T>`, `List<T>`, `HashSet<T>`)
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
ADR 0062 builds arrays from a JSON array and objects by constructor or fields, but a parameter or field of a constructed
generic type gets neither: a JSON array is accepted only for an array type, a `{"constructor": ...}` object cannot name
`List<string>`, and `"type"` resolves only non-generic metadata names. P2-247
(`docs/runs/2026-10-20-precision-execute-verdict-2.md`) left three results undetermined on it: Git Extensions
`RefsFormatter::FilterAndFormatBranches(IEnumerable<string>,bool,bool)` (EQ002), OpenRA
`RotationPaletteEffect::StartsWithAny(string,HashSet<string>)` (P2-237's #2), and the sampled exposure OpenRA
`ScreenShaker::GetScrollOffset()` (a `List<ShakeEffect>` field, and a `WorldRenderer`). A fourth, Duplicati
`TestUtils::IndexOf(List<string>,string)`, was refuted by hand trace without it.
When done, a parameter or field of type `IEnumerable<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `List<T>` or
`HashSet<T>` with a buildable `T` is built from a JSON array (by `probe`) and from the model's array value (by replay),
as a `List<T>` or a `HashSet<T>`.

## Spec references
ADR 0062 (decision 3, argument kinds), `src/Equiv.Frontend.CSharp/Execution/DriverSource.cs`, `ProbeArguments`,
`docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `samples/` pair whose Divergent takes an `IEnumerable<string>` is `reproduced` under `--execute`, and `probe` calls a
   member taking a `HashSet<string>` with a JSON array.
2. The three undetermined results above are probed and each one's outcome is given, in Notes.

## Notes
- Filed 2026-10-20 by P2-247 (the undetermined obstacle its sub-agents met on three pairs).
