# P2-262 Precision bug: the signed-zero and NaN rows of `Math.Min`, `Math.Max` and `Math.Ceiling` fire where the data flow rules their witness out
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
.NET Core 3.0 made `Math.Min`/`Math.Max` on `float` and `double` IEEE 754-2019 (`-0` below `+0`, NaN propagated) and
`Math.Ceiling` of a small negative value `-0`. The rows fire on every unedited call. P2-247
(`docs/runs/2026-10-20-precision-execute-verdict-2.md`, appendix C) found 5 sampled exposures where no operand can be
`-0` or NaN, all false positives:
- OpenRA `ProductionTabsWidget::Scroll(int)` and `FinalizedUIModelRenderable::Screen3DBounds(WorldRenderer)`: one
  operand starts at `+0` or `float.MaxValue` and is only ever a sum with a `+0` or an `int` converted to `float`;
- OpenRA `ServerListLogic::GetStateLabel(GameServer)`: a `PlayTime > 0` guard keeps `Math.Ceiling`'s argument positive;
- Duplicati `ThrottledStream::DelayIfRequired(...)` and `Scheduler::Runner()`: a positive `TimeSpan`'s milliseconds,
  checked just before, against a positive constant, then cast to `int`.
When done, a `Min`/`Max` operand that is an integer converted to floating point, a positive constant, or a value a
dominating guard bounds away from zero and NaN is no witness of the row, and the row fires only when both operands can
be equal zeros of opposite sign or one can be NaN.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the `Math` rows), `RowPreconditions`, ADR 0053 (floating point as
IEEE sorts), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Unit tests: `Math.Max(x, (float)i)` with `x` a sum from `+0`, `Math.Min(t.TotalMilliseconds, 2000)` under
   `t > TimeSpan.Zero`, and `Math.Ceiling(p / 60.0)` under `p > 0` fire no row; `Math.Max(a, b)` of two parameters does.
2. `openra-17989` and `duplicati-3124` in `full`: each of the 5 results is given with its result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247.
