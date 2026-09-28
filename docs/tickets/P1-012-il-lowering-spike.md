# P1-012 Spike: how much of the opaque tail disappears if the fallback lowers from IL?
Status: in-progress
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
- Result: 114 of 1,143 changed pairs (10.0%) lower from ILAst with only mapped kinds; shape drift 19 (1.7%), under half the gain. Criterion 4 therefore writes ADR 0039 (proposed), not a ROADMAP line. All 19 drift pairs are API binding changes (new BCL overloads, a `Deconstruct` bound to an extension on legacy and to an instance method on modern, a `ref`-returning matcher), not codegen. 6 of them are hidden from ADR 0024's serialisation. Report: `docs/runs/2026-09-28-il-lowering-spike.md`.
- Corpus: reused an existing restored `.corpus/` from another worktree on this box (same pinned commits) instead of re-fetching Git Extensions; the SARIF join uses that worktree's latest full run (P2-033 branch, 2026-09-27).
- ICSharpCode.Decompiler 11.1 has no public `PEFile` type name reachable from `ICSharpCode.Decompiler.Metadata` under the `using` of `ICSharpCode.Decompiler.CSharp` (the compiler reports it as a namespace); fully qualify it. `IAssemblyResolver` also needs `BeginSnapshot()`.
- Decision: population -> the changed pairs (ADR 0024: serialisations differ, or either side runtime-sensitive) holding an opaque whose fingerprint the other side does not also hold, lowered at this branch's `main`. Alternatives: every changed pair holding any opaque (the census's superset, which counts pairs M4-004 already shares); only the pairs a full run reported Unknown(opaque). Rule: "still opaque after the kept M4 tickets" names M4-004's sharing, and a full run is at an older commit. The SARIF join is still printed as a subset.
- Decision: ILAst level -> `CSharpDecompiler.GetILTransforms()` without the transforms that rebuild a C# construct (async, iterators, `using`, `lock`, lambdas, local functions, display classes, string and nullable switches, patterns, and the statement transforms). What stays structures control flow, inlines stack slots and splits variables. Alternatives: the raw `ILReader` output (no structure); the full pipeline, which would reintroduce `UsingInstruction`, `LockInstruction` and `Await`, the very constructs the fallback exists to avoid. Rule: the goal says the compiler "has already expanded" these constructs.
- Decision: mapping table -> follows `IOPERATION-COVERAGE.md`'s own semantic refusals. Unboxing, a caught exception object read, a `ref` local, `throw` of a non-`new` value, `default` of a type parameter, and lambdas and local functions (their bodies are elsewhere and their names are ordinals) stay unmapped. A method group (`ldftn` of a named method) is a designated constant. Addresses are mapped only where they are read or written through, or passed to a call. Alternatives: map every opcode that has any IR analogue. Rule: the fallback must not be less sound than IOperation where IOperation declined for a reason, not for syntax.
- Decision: emit -> each project's `Compilation` emitted in memory with its own options, as M4-009 emits for replay, and resolved against its own references. Alternatives: the binaries a build produces. Rule: an IL fallback inside equiv would read the loaded compilation, so both sides are compiled by the same Roslyn, and drift comes only from `LangVersion` and the target BCL.
- Decision: token-identical C# -> the declaration's tokens without trivia; a constructor compares its whole type declaration, since its IL holds the field initialisers. Drift is split into text drift (the canonical ILAst differs: offsets, labels and variable names renumbered) and shape drift (the opcode tree differs). Criterion 4 uses shape drift. Rule: operand-only differences on token-identical C# are a changed callee or type, which IOperation lowering sees too.
