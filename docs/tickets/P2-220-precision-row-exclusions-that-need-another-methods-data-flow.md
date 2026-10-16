# P2-220 Precision: three row exclusions need data flow from another method
Status: in-progress
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
- Criterion 1, what each of the three needs, read from the legacy checkouts (`openra__openra@5e74e58b2204`,
  `duplicati__duplicati@beaf03562fdc`, P2-212's):
  - `FieldLoader::Load(object,MiniYaml)`: `fli.Field.SetValue(self, val)`, `fli` an element of `TypeLoadInfo[self.GetType()]`.
    `TypeLoadInfo` is a `static readonly ConcurrentCache<Type, FieldLoadInfo[]>` built with `BuildTypeLoadInfo`, whose
    only filler is `foreach (var ff in type.GetFields(Public | NonPublic | Instance))`, each `ff` passed to
    `new FieldLoadInfo(field, ...)`. The flags include `Instance` and not `Static`, so no static field reaches the call.
    Clearing it takes four reads, not one: the cache's indexer (`ConcurrentCache<T, U>`, a public class of another file)
    returns only what its factory returns; the factory is the method group in the field's initializer; `Field` is a
    `readonly` field of `FieldLoadInfo` that its constructor sets from a parameter; and every `new FieldLoadInfo(...)`
    passes an instance field. The last is every caller of a public constructor of a public nested type, which is a
    search of the whole solution.
  - `OAuthHttpClient::.cctor()`: `field?.SetValue(null, true)` on `typeof(Uri).GetField("useDotNetRelativeOrAbsolute",
    Static | GetField | NonPublic)`, the one case the row is about, inside `if (Utility.Utility.IsMono)`. `IsMono` is
    `public static bool IsMono => Type.GetType("Mono.Runtime") != null;` (`Duplicati/Library/Utility/Utility.cs`, another
    project). On both runtimes it is false: `[Type]::GetType("Mono.Runtime")` is null on .NET Framework 4.8
    (4.0.30319.42000, Windows PowerShell 5.1) and on .NET 10.0.13 (PowerShell 7), measured 2026-10-16. Only Mono, which
    is neither runtime a pair names (ADR 0040), defines `Mono.Runtime` in its core library. Clearing it takes one read:
    the getter's body.
  - `SpriteFont::MaxLineWidth(string[],Func<string, float>)`: `maxWidth = Math.Max(maxWidth, lineWidth(line))` in a
    `foreach`, then `return maxWidth;`. Its only caller is `Measure(string)`, `new int2((int)Math.Ceiling(MaxLineWidth(lines,
    lineWidth)), ...)`, which maps -0 and +0 to 0. But the method's own result is not the same on both runtimes:
    `MaxLineWidth(["a"], _ => -0f)` returns -0 on .NET Framework (`Max(+0, -0)` returns the second zero) and +0 on .NET.
- Decision: the shape taken is the solution guard (`OAuthHttpClient::.cctor()`). Each of the three clears one result, so the
  count does not discriminate; the guard is the only one one method's read clears. Alternatives: the cache filler (four
  reads, the last a search for every caller of a public constructor: the size guard), the only caller (below). Rule: 4.
- Decision: the only caller (`MaxLineWidth`) is not taken. Its result is -0 on .NET Framework and +0 on .NET for an input
  its caller can pass, so an Equivalent for it would say a procedure returns the same value when it does not. ADR 0019's
  P2-207 clarification lets a private method's Equivalent cover the inputs its callers can give it, not the uses they
  make of its result; reading it that way is a new decision, and even then the read is four steps (a local through a
  loop, its return, the one caller, and `Math.Ceiling` before the conversion). The caller, `Measure`, is already
  Equivalent-ready: its `Ceiling` result is converted to `int` at once. Alternatives: an output-side clarification of ADR
  0019 (not this ticket's, and not a precision fix: it changes what a private method's verdict means). Rule: 4.
- Decision: the cache filler (`FieldLoader::Load`) is not taken: the size guard, as above. Alternatives: reading
  `FieldLoadInfo`'s constructor's callers in its own file only (a public constructor of a public type can be called from
  any project, so that is unsound). Rule: 4.
- Decision: the guard is a Mono check: `Type.GetType("Mono.Runtime") != null` (either operand order), or a reference to a
  solution property whose getter returns only Mono checks, read as P2-200 reads a returned call (the body that runs, at
  most `ValidPaths.CalleeDepth` getters deep), and the call to the row's member lies in the branch an `if` or a
  conditional expression takes when the check is true. The constant name must be `Mono.Runtime`, the overload
  `Type.GetType(string)`, and no assembly the compilation that makes the lookup references, its own included, may define
  a `Mono.Runtime` type (`Type.GetType(string)` searches the calling assembly and the core library). Alternatives: any
  `Type.GetType` of a name no reference defines (an assembly loaded at run time could define it), the guard's value read
  by the solver (the lowering suppresses rows per method, before any solver runs). Rule: 4.
- Decision: the guard narrows `staticFieldSet` only. Alternatives: every row with a call fact (the same argument holds, but
  no result in the three needs it, and the ticket names this row). Rule: 4.
- Decision: `ValidPaths` exposes the values a solution callee's body returns (`ReturnedValues`, the first half of
  `ReturnsOnlyValidPaths`) and `CalleeDepth`, rather than `RowPreconditions` getting a second copy of `Dispatched`, `Body`
  and `Returned`. Alternatives: a copy. Rule: 4.
