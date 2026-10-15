# P2-220 Precision: three row exclusions need data flow from another method
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-190 (done) narrowed rows to the calls their change can reach by reading one method, and kept two results on the
size guard "data flow beyond one method". P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`) counts
both of them still EQ006 and finds a third of the same kind:
- `openra-17989`, `OpenRA.FieldLoader::Load(object,OpenRA.MiniYaml)`, in P2-212's sample: the `FieldInfo::SetValue(`
  row (`staticFieldSet`) on a `FieldInfo` read from a static cache whose only filler takes every field from
  `GetFields` with flags that include `Instance` and not `Static`, so no static readonly field reaches the call;
- `duplicati-3124`, `Duplicati.Library.OAuthHttpClient::.cctor()`, P2-130's false positive that P2-190 kept, not in
  P2-212's sample: the same row behind `Utility.IsMono`, a solution member that is false on both runtimes;
- `openra-17989`, `OpenRA.Graphics.SpriteFont::MaxLineWidth(string[],System.Func<string, float>)`, in P2-212's
  sample: the signed-zero `Math::Max(float,float)` row on a private method whose only caller converts the result
  with `(int)Math.Ceiling(...)`, which maps -0 and +0 to 0 (`signedZeroResult` reads the same method only).

When done, the exclusion that clears the most of these is read across the one method it needs, or `equiv-adr`
records why none is.

## Spec references
`docs/tickets/done/P2-190-*.md` (the size guard and the two kept results), `src/Equiv.Core/RuntimeChanges/runtime-changes.json`
(`staticFieldSet`, `signedZeroResult`), `docs/tickets/done/P2-200-*.md` (how a callee chain is read, bounded and
argued sound), ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. Notes give each of the three what it needs: a cache filler's flags, a solution guard's value on both runtimes, a
   private method's only caller.
2. The one shape taken is read with P2-200's bound and soundness argument; the others are decided with `equiv-decide`.
3. `openra-17989` and `duplicati-3124` rerun in compare mode quick; Notes give the three procedures' new results.

## Tests
The runtime-change rule tests; a sample per shape taken, and one where the other method lets the change through.

## Size guard
A whole-solution call graph: stop.

## Out of scope
The single-method shapes (P2-216).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
