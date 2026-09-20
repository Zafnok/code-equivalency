# ADR 0015: A call's heap effect and array aliasing are named limits, not defects

Status: proposed (2026-09-20)

## Context
M2-004 (PR #30) lowers fields and arrays as SSA maps, per its acceptance criterion 6. Two gaps
follow. An `IrCall` does not havoc any `field.*` map, so `Foo(o); x = o.F;` and `x = o.F; Foo(o);`
encode identically. And `array.<v>` is keyed per array *variable*, not per array value, so
`void M(int[] a, int[] b)` treats `a` and `b` as disjoint even when the caller passes one array
twice. Both are what the acceptance criteria asked for, both are unsound in general, and both can
only produce a false Equivalent. No existing test obligation covers either: VERIFICATION-MODEL
section 7's soundness harness generates *IR* and checks `verify(P, P)` and `verify(P, mutate(P))`,
so it is blind to a C#-to-IR lowering gap by construction, and the one obligation that does cover
C#-to-IR, the lowering oracle, generates no field write around a call and no aliased array
(`tests/Equiv.TestSupport/LoweringOracleGen.cs`). Reviewing PR #30 added a paragraph stating both
gaps to VERIFICATION-MODEL section 2 (commit `4df213f`).

## Decision
Both gaps are named limits of the MVP heap model rather than defects of M2-004, and each gets its
own ticket: P1-005 havocs every `field.*` map an `IrCall` could reach, P1-006 keys array maps per
array value. Until both land, M3-001's soundness harness going green is not evidence that the C#
frontend is sound, and M3-001's acceptance criteria say so. The section 2 paragraph from `4df213f`
stays as the doc-level statement of the limit; accepting this ADR extends it with the two ticket
ids and adds the M3-001 line.

## Why
- Section 1 lists three ways a pair fails to be Equivalent with confidence: a failed ladder rung,
  a timeout, a reached `IrOpaque` (ADR 0014). These gaps are a fourth, and it is silent: no
  `IrOpaque`, no `properties.opaqueNodes` entry, no Unknown, nothing in the SARIF.
- The one thing this project trades everything else away for is no false Equivalent. A limit that
  can *only* produce one belongs where limits are recorded.
- The gap is in lowering and the harness that will look sound runs over IR. Naming that in
  Consequences is the only mechanism that makes M3-001 carry the caveat and makes P1-005/P1-006
  extend the oracle generator instead of trusting the harness.
- Cost is one docs PR. The alternative is a false proof that no gate can see.

## Rejected
- Two entries under the ROADMAP's *Not yet owned by any ticket* list: that list holds process
  chores (SonarQube promotion, a real SARIF schema validator, a licence expiry). A soundness limit
  reads as a chore beside them, and a ROADMAP bullet binds no ticket's acceptance criteria.
- Leaving the section 2 paragraph as the whole record: it says what is not modelled, but not who
  closes it, and nothing makes M3-001 read it.
- Closing either gap inside M2-004: havocking `field.*` on a call needs a story for which fields a
  callee can touch, and per-value array maps need an alias analysis. Each is its own ticket, and
  either breaks M2-004's size guard.
- Making a call's heap effect `IrOpaque` instead: sound, and it makes every procedure containing a
  call Unknown. The MVP's target is exactly code with calls.
- Deciding this under `equiv-decide` as a spec gap-fill: the skill allows that only when the choice
  does not change what a verdict means, and this narrows Equivalent as section 1 defines it.

## Consequences
- Easier: M3-001 encodes maps as M2-004 built them, with the limit recorded and owned rather than
  rediscovered mid-milestone.
- Harder: Equivalent on a procedure that writes a field around a call, or that takes two array
  parameters, is conditional on an assumption until P1-005 and P1-006 land. A reviewer reading
  those verdicts has to know that.
- Docs on acceptance: VERIFICATION-MODEL section 2 (the `4df213f` paragraph gains the two ticket
  ids) and section 7 (the soundness harness runs over IR and does not cover lowering gaps).
- Tickets on acceptance: M3-001 gains an acceptance criterion scoping its soundness-harness claim
  to the encoder; P1-005 and P1-006 are written; P1-004's element-map work inherits the aliasing
  limit and says so.
- P1-005 and P1-006 each extend `LoweringOracleGen.cs` with the case that catches its own gap: a
  field write around a call, and one array passed as two parameters. Adding either case before its
  fix would fail the oracle, which is what makes it the right test.
