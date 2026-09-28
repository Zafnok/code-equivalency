# ADR 0039: A pair that IOperation leaves with an unshared opaque is lowered from ILAst instead; IOperation stays primary

Status: accepted (2026-09-28). Proposed by P1-012 the same day. Acceptance added three things to the
proposed text: identities resolved through the loaded compilation (so the lowering lives in
`Equiv.Frontend.CSharp`), off by default until P1-018, and an IL mode of M0-012's gate. The per-pair, both-sides, IOperation-primary decision is unchanged.

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
Every type and member the ILAst names is resolved back to the loaded compilation's symbol (by its
documentation ID) and lowered by the same identity and sort code the IOperation lowering uses, so a
call has one `ProcedureIdentity` whichever lowering produced it. `runtime-changes.json`,
`api-equivalences.json`, callee matching (ADR 0019) and contracts (ADR 0036) therefore apply to an
IL-lowered pair unchanged. A reference that cannot be resolved is an opaque, never a new identity.
The fallback ships off by default (`--il-fallback`). It is turned on by default only by a corpus run
in which the pairs it moves out of Unknown(opaque) to a decided verdict are at least 5% of changed
pairs (ADR 0028's bar, applied to verdicts rather than to lowerability). Each procedure in SARIF
records which lowering it used (`properties.lowering`: `operation` or `il`).

## Why
- Measured: 10.0% of changed pairs gain a lowering with no opaque (5.2% among the pairs the last
  full run left Unknown(opaque)). Six-plus construct tickets would be needed for the same pairs,
  and no one of them clears ADR 0028's bar alone (lifted `Binary`, the largest, is 46 pairs, 4.0%).
  So the per-construct route cannot schedule this tail at all.
- ADR 0003's objection does not hold where the fallback runs. Both sides are compiled by the one
  Roslyn that loads them, so compiler version cannot cause drift. The 19 shape-drift pairs are new
  BCL overloads, a changed `Deconstruct` binding and a `ref` return. IOperation sees the first as a
  different callee too. IL additionally sees the other two, which ADR 0024's serialisation hides.
- Drift cannot make a false Divergent: a binding change is two different uninterpreted callees, which
  is an abstraction, so ADR 0026 makes such a pair Unknown(Abstraction), never EQ002.
- Resolving identities through the loaded compilation is what keeps the fallback as sound as the
  primary lowering. An IL-derived identity that differed from the IOperation one by even a nullable
  annotation (P2-042) would miss a `runtime-changes.json` row, which is a silent false Equivalent.
- Off by default until measured, because lowerable is not proved: lifted operators become
  `Nullable<T>` getter calls and interpolation `string.Format` calls, which stay uninterpreted.
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
- A separate `Equiv.Frontend.Il` project now: without Roslyn it would need a resolver interface over
  the type, identity, heap-map and pure-catalogue code, which duplicates exactly the seam that must
  not drift. VB.NET compilations are Roslyn too, so the first extra language does not need it.
- Identities built from ILSpy's own type system: a second identity scheme beside `RoslynIdentity`
  that every catalogue lookup would have to agree with.
- On by default as soon as it lowers: the spike measured lowerable pairs, not decided verdicts.

## Consequences
- A new dependency category: a decompiler library in the shipped product. `ICSharpCode.Decompiler`
  (MIT) moves from ADR 0002's spike row to a product row, and the licence gate scans it.
- The IL lowering lives in `Equiv.Frontend.CSharp` (`Lowering/Il/`), the only project that
  references `ICSharpCode.Decompiler` (an architecture test says so). It calls the existing
  `TypeMapper`, `CallIdentityFactory`, `HeapInputs`, `PureCatalogue` and `SsaBuilder` directly, so
  "the same identity and sort code" is a call, not a contract two projects must keep. A separate
  Roslyn-free `Equiv.Frontend.Il` waits until a frontend without a Roslyn compilation needs it. It needs its own coverage table
  (`docs/tickets/IL-COVERAGE.md`) beside `IOPERATION-COVERAGE.md`, an oracle test against compiled
  code as `LoweringOracleTests` has, the 100% gate, and a mode of M0-012's differential gate that
  forces the IL lowering on both sides.
- Lowerable is not proved. The IL routes lifted operators through `Nullable<T>` getters and
  interpolation through `string.Format`, which stay uninterpreted calls without catalogue entries.
  P1-018 measures the decided-verdict gain on the corpus and is the only ticket that may turn the
  fallback on by default.
- Lambdas (132 of the 179 pairs that stay opaque) are not helped, and remain their own problem.
- ADR 0003 gains a "superseded in part by 0039" status line, and ARCHITECTURE.md's frontend section
  and VERIFICATION-MODEL.md sections 3 and 6 gain the fallback. Tickets P1-014 to P1-018 implement it.

