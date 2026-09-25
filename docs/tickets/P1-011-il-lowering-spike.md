# P1-011 Spike: how much of the opaque tail disappears if the fallback lowers from IL?
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M4-001, M4-002, M4-004, P2-001

## Goal
Most of the opaque reasons on Git Extensions are C# syntax that the compiler has already expanded
into plain IL: `switch-pattern`, `InterpolatedString`, `DelegateCreation`, `IsNull`, `DefaultValue`,
`EventAssignment`, `ref-argument`, `lock`, and so on. ICSharpCode.Decompiler's ILAst (MIT, the
ILSpy engine) is a typed, structured IR built from IL. A fallback that lowers a
method from its ILAst when IOperation lowering is opaque would reach those constructs without one
ticket per construct. It would also cover VB.NET and F# assemblies, and BCL bodies. ADR 0003
chose IOperation, so this spike measures before any ADR is written:
- among Git Extensions' changed pairs still opaque after the kept M4 tickets, how many does an
  ILAst-based lowering reach with no opaque node?
- does it break congruence, because the two compilers emit IL of different shapes?

## Spec references
ADR 0003 (lower from IOperation); ADR 0028 (5% bar); ADR 0034 (reason sets); ADR 0002 (new package
rows).

## Acceptance criteria (all must hold; nothing beyond them)
1. ADR 0002 gains a row for `ICSharpCode.Decompiler`, scope "spike tool only, not shipped", in
   this PR.
2. `tools/spikes/il-lowering/` emits Git Extensions' project compilations (as M4-009 does). For
   each changed pair still opaque after the kept M4 tickets, it decompiles both methods to ILAst
   and counts:
   - the pairs whose ILAst uses only instruction kinds a small mapping table could lower to
     existing IR constructs (the table is in the report);
   - the pairs where the legacy and modern ILAst differ although the C# is token-identical
     (compiler shape drift);
   - async and iterator state machines, as their own bucket.
3. `docs/runs/<date>-il-lowering-spike.md` reports those counts as shares of changed pairs, and
   the ten most frequent ILAst instruction kinds with no mapping.
4. If the lowerable gain is at least 5% of changed pairs **and** shape drift is under half of
   that gain, write `docs/adr/00nn-il-fallback-lowering.md` (proposed): IL as the fallback only,
   with IOperation primary. Otherwise add one line with the numbers to ROADMAP's post-MVP list.
5. Nothing under `src/` changes.

## Files
`docs/adr/0002-dependencies.md`, `tools/spikes/il-lowering/**`,
`docs/runs/<date>-il-lowering-spike.md`, and the ADR or the ROADMAP line.

## Tests
None required: it is a spike.

## Size guard
Writing an IR lowering from ILAst is the feature, not the spike. Stop at counting.

## Out of scope
Java bytecode. Shipping the package. Replacing IOperation.

## Notes
