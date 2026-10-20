# P2-272 Check: do `Math.Sin` and `Math.Cos` differ between .NET Framework 4.8 and .NET 5 on ordinary arguments, which the runtime table does not cover?
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
The runtime-changes table's trigonometric row names only arguments beyond 2^63, where .NET Framework's x64 `Math.Sin`
returns its argument unreduced (P2-247 confirmed OpenRA's `float2::FromAngle(float)` there). P2-247's OpenRA exposure
sub-agent measured, outside equiv, `Math.Sin(Math.PI)` and about half of 20,000 random angles giving results that differ
in the low bits after rounding to `float` between Windows PowerShell 5.1 (.NET Framework 4.8) and pwsh (.NET 10),
`docs/runs/2026-10-20-precision-execute-verdict-2.md`. If .NET 5 differs from .NET Framework 4.8 the same way, an
unedited body that computes with `Math.Sin`/`Math.Cos` on ordinary angles is congruent today and proved Equivalent while
the real runtimes disagree: a false Equivalent. When done, the difference is measured between .NET Framework 4.8 and
.NET 5.0.17 (and .NET 6, 8, 9 for the upgrades), and either the table gains the rows the measurement shows (ADR 0040),
with a `P2-nnn-soundness-` ticket filed if any pair's result moves, or the Notes record that the runtimes agree.

## Spec references
ADR 0040, `src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the `Math` rows), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A measurement file under `docs/runs/` gives, for `Math.Sin`, `Math.Cos`, `Math.Tan` and their `MathF` forms, the share
   of a fixed set of arguments whose results differ between .NET Framework 4.8 x64 and each of .NET 5, 6, 8 and 9 x64.
2. If any share is above zero, the table gains the row (with its runtimes) and a test shows an unedited call is no
   longer congruent; otherwise nothing else changes.

## Notes
- Filed 2026-10-20 by P2-247. Not yet a soundness ticket: the difference was seen against .NET 10, not against a pair's
  runtime.
