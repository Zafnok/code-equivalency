# ADR 0039: A pair that IOperation leaves with an unshared opaque is lowered from ILAst instead; IOperation stays primary

Status: proposed (2026-09-28)

## Context
ADR 0003 lowers from IOperation, and each unsupported construct stays `IrOpaque` until a ticket
lowers it. Spike P1-012 (`docs/runs/2026-09-28-il-lowering-spike.md`) measured the tail M4 leaves on
Git Extensions. 318 of 1,143 changed pairs (27.8%) still hold an opaque that the other side does not
share. When both methods are read back from IL as ILSpy's ILAst, 114 of them (10.0% of changed
pairs) use only instruction kinds that a 46-key table maps onto constructs the IR already has.
Those pairs' reasons are lifted `Binary`, `Conversion`, `CompoundAssignment`, `switch-pattern`,
`InterpolatedString` and `DeconstructionAssignment`, each of which is one ticket per construct
today. ADR 0003 rejected IL because the two sides "compile the same source to different IL". The
spike found shape drift in 19 pairs (1.7%). Every one of them is an API binding change, not codegen.
Both of criterion 4's conditions hold: the gain is at least ADR 0028's 5%, and the drift is under
half the gain.

## Decision
IOperation lowering stays primary, and congruence (ADR 0024) is still decided on its fingerprints
first. When a matched pair is not congruent and either side's IOperation lowering holds an opaque
whose fingerprint the other side does not share, **both** sides are lowered again from ILAst.
Lowering both keeps the pair in one lowering. The ILAst lowering replaces the IOperation bodies only
if it holds fewer unshared opaques. The ILAst comes from each side's loaded `Compilation`, emitted in
memory with a portable PDB (source spans come from its sequence points). It is read by
`ICSharpCode.Decompiler`'s `ILReader` and the structural transforms only (P1-012's pipeline, which
rebuilds no C# construct). It is lowered by P1-012's mapping table, and every unmapped instruction
is an `IrOpaque` whose reason is its ILAst key. The table follows the IOperation lowering's semantic
refusals: unboxing, a caught exception object, `ref` locals, lambdas and local functions stay opaque.
Each procedure in SARIF records which lowering it used (`properties.lowering`: `operation` or `il`).

## Why
- Measured: 10.0% of changed pairs gain a lowering with no opaque (5.2% among the pairs the last
  full run left Unknown(opaque)). Six-plus construct tickets would be needed for the same pairs.
- ADR 0003's objection does not hold where the fallback runs. Both sides are compiled by the one
  Roslyn that loads them, so compiler version cannot cause drift. The 19 shape-drift pairs are new
  BCL overloads, a changed `Deconstruct` binding and a `ref` return. IOperation sees the first as a
  different callee too. IL additionally sees the other two, which ADR 0024's serialisation hides.
- It reaches VB.NET and F# bodies and BCL bodies later without a new frontend per language.
- Kept narrow on purpose: it is a fallback per pair, never a replacement, so every proof IOperation
  already gives is unchanged.

## Rejected
- Replacing IOperation with IL: this would lose ADR 0024's fingerprints and IOperation's
  nullability and pattern structure for the 72% of changed pairs that hold no unshared opaque today.
- A fallback on one side only: the two sides would then be lowered differently, and a pair would
  mix two lowerings.
- The full ILSpy pipeline: it rebuilds `using`, `lock`, `await` and lambdas, the constructs the
  fallback exists to avoid.
- Mapping everything that has an IR analogue (unboxing, caught exceptions): that is less sound than
  IOperation where IOperation declined for a reason.

## Consequences
- A new dependency category: a decompiler library in the shipped product. `ICSharpCode.Decompiler`
  (MIT) moves from ADR 0002's spike row to a product row, and the licence gate scans it.
- The IL lowering lives in the frontend (language-neutral IL, so a project such as
  `Equiv.Frontend.Il` referenced by `Equiv.Frontend.CSharp`). It needs its own coverage table beside
  `IOPERATION-COVERAGE.md`, oracle tests against compiled code as `LoweringOracleTests` has, and
  the 100% gate.
- Lowerable is not proved. The IL routes lifted operators through `Nullable<T>` getters and
  interpolation through `string.Format`, which stay uninterpreted calls without catalogue entries.
  The first implementation ticket must measure the Equivalent gain on the corpus, not the lowerable
  share.
- Lambdas (132 of the 179 pairs that stay opaque) are not helped, and remain their own problem.
- If accepted: ADR 0003 gains a "superseded in part by 0039" status line, and ARCHITECTURE.md's
  frontend section and VERIFICATION-MODEL.md gain the fallback. The tickets are written then, and
  none are written before.
