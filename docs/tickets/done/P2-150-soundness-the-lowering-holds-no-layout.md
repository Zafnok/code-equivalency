# P2-150 Soundness: the lowered body of an operation that reads a type's layout holds none of it
Status: done (PR #452)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-149

## Goal
P2-149 put the declaration of a type into the text of a body that reads its layout, so a pair whose
declarations differ is no longer congruent (ADR 0024, clarification of 2026-10-09 (P2-149)). Such a
pair is then lowered and goes to the solver, and the IR holds no layout. Two shapes are lowered as
if the layout did not exist:

- a field of a `[StructLayout(LayoutKind.Explicit)]` type is its own `field.<Type>.<Field>` map
  (`docs/tickets/IOPERATION-COVERAGE.md`, `FieldReference`). Two fields at one `[FieldOffset]` are
  one storage location, so `s.A = 1; return s.B;` and `int b = s.B; s.A = 1; return b;` differ and
  the model says they are equal. This holds on a pair whose two declarations are the same;
- a call that is handed a type and reads its layout (`Marshal.SizeOf<S>()`, `Unsafe.SizeOf<S>()`,
  `MemoryMarshal.Cast<byte, S>(...)`) is an `IrCall` both sides share by its identity, which names
  `S` and nothing of its declaration. On a pair whose declarations of `S` differ and whose bodies
  differ elsewhere, the two calls are one function.

The other operations of P2-149's rule (`sizeof` of a user-defined struct, a pointer, a call through
a function pointer, an inline array) are believed to be opaque fragments, whose sharing the
fingerprint now decides. `sizeof` is tested; the rest is read off the coverage table.

Found while working P2-149 by reading the coverage table; none was reproduced as a wrong result.
The first step is the repro.

## Spec references
ADR 0024 (the clarification of 2026-10-09 (P2-149), "The solver path needs the same fact"), ADR 0025
(a side-specific function), ADR 0014 (an opaque), `docs/tickets/IOPERATION-COVERAGE.md`
(`FieldReference`, `Invocation`, `SizeOf`), `.claude/skills/equiv-extend-ir`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Layout`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Each bullet of the Goal is a test that shows an Equivalent the solver should not give, or the
   bullet is struck with the test kept. For a pointer indirection, a call through a function
   pointer and an inline-array access, a test shows the operation is an opaque whose fingerprint
   differs when the declaration does, or the operation joins the Goal.
2. Before any change to the lowering: decide through `.claude/skills/equiv-adr`'s bar test what a
   lowered body does with each (an opaque with its own reason, a model of the shared storage, a
   side-specific function when the declarations differ), and record which row applied in `## Notes`.
3. The decision is implemented, and `docs/tickets/IOPERATION-COVERAGE.md` says so in the rows it
   changes. A field of a type with no explicit layout lowers as it does today; a test asserts it.
4. The public-corpus results that change are counted in `## Notes`, by rule id before and after,
   from one run of `powershell-19687` in compare mode quick on each side of the change.

## Files
`src/Equiv.Frontend.CSharp/Lowering/` (with `Layouts.cs`, the rule, and `Il/IlFallback.cs`), its
tests, `src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (it reads the rule from
`Layouts`; its text is unchanged), `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/VERIFICATION-MODEL.md`, and the clarification under ADR 0024 that criterion 2 produced.

## Tests
In `IrLowererTests`: one per bullet of the Goal and one per operation criterion 1 names, and
`AFieldOfATypeWithoutAnExplicitLayoutIsStillItsMap`. In `LayoutEquivalenceTests` (integration): the
Equivalent from the solver that criterion 1 asks for. `LayoutsTests` and `IlFallbackTests` for the
rule and for the IL lowering.

## Size guard
One pull request after the decision. If criterion 2 decides a model of shared storage, stop after
the opaque and file the model.

## Out of scope
- The fingerprint (P2-149).
- A type from a reference.
- A generic member of the solution that reads its type parameter's layout (P2-151).

## Notes
- Filed 2026-10-09 from P2-149.

### Criterion 1: the repro
- Both bullets of the Goal are reproduced as an Equivalent from Z3 on `main` at `6a12ae27`, and
  none is struck. `LayoutEquivalenceTests` (integration) failed ten of its twelve cases before the
  change, each with `Actual: Equivalent`:
  - a write to one field of an explicit layout and a read of another at the same offset, against
    the read first, beside one declaration: through `=`, `+=`, `++` and a deconstruction, and
    through two auto-properties whose backing fields share an offset;
  - `Marshal.SizeOf<S>()`, `Marshal.SizeOf(typeof(S))`, `Unsafe.SizeOf<S>()`,
    `MemoryMarshal.Cast<byte, S>(...)` and `Unsafe.SizeOf<S>()` beside a `new Span<S>(...)`, each
    beside two declarations of `S` in bodies that differ by the order of a sum.
  The other two cases assert that the call is still shared beside one declaration, and held.
- The operations the ticket read off the coverage table are opaque, and none joins the Goal:
  `(*p).B`, `p->B`, `p[1].B`, `((S*)p)->B`, `p + 1`, `q - p`, `p++`, a call through a
  `delegate*<S, int>` and an inline-array access each lower to an opaque, and no fingerprint of the
  body is the same beside another declaration
  (`AnOperationThatReadsMemoryIsAnOpaqueWhoseFingerprintHoldsTheDeclaration`). Pointer arithmetic
  is not in the ticket's list; it scales by the size, so it is tested with them.
- Found by the repro, not in the Goal: an auto-property and a field-like event of an explicit
  layout are storage at an offset too (`[field: FieldOffset(n)]`). Lowered, the property was its
  backing field's map and the event its accessor call, with the same wrong Equivalent.

### Criterion 2: the decision
- `equiv-adr` bar test, row 1: ADR 0024 already decides what an operation the lowering does not
  model is (an opaque, shared by its fingerprint, decision 2), and its clarification of 2026-10-09
  (P2-149) left this case to the lowering. So the vehicle is a dated clarification under ADR 0024,
  2026-10-09 (P2-150), in this pull request. No new ADR: no Core contract, verdict, rule id or
  SARIF shape changes.
- Decision: what a lowered body does with both bullets -> an opaque with reason `Layout`, shared by the fragment's fingerprint, which holds the declaration since P2-149. Alternatives: a model of the shared storage (needs every size and packing rule per runtime; the size guard files it, and nothing measured asks for it), a side-specific function (the lowering reads one side, so the declaration would go into the call's identity, and two identities are a trace that differs: Divergent where nothing is known). Rule: 1 (soundness first), then 4.
- Decision: why sharing the opaque is sound beside one declaration -> it is one call on both sides and a call's place in the trace is observable, so a write of `A` before a read of `B` is not proved equal to the read first; every read of that storage is such a call. Alternatives: never share it (loses every pair that uses such a field the same way on both sides). Rule: 2.
- Decision: an auto-property or an event of an explicit layout -> the same opaque with no fingerprint, never shared, and no accessor call. Alternatives: share it as a field's (no fingerprint holds the declaration, because the bound tree does not name the backing field), leave it (the repro above). Rule: 1. The fingerprint's side of it is P2-152.
- Decision: which calls -> exactly those the fingerprint follows with a declaration: the callee is under the two namespaces or takes a pointer, and a type of the solution is among the types P2-149 lists for it, or the assembly has a `[DisableRuntimeMarshalling]`. Alternatives: every call into the two namespaces (an interpolated string's handler, an awaiter and `Unsafe.As` on framework types would stop being calls for no declaration that could differ), a list of members (goes stale; a missing member is a false Equivalent). Rule: 4.
- Decision: where the rule lives -> `Lowering/Layouts.cs`, read by `BoundSerialiser.Layout` and by `IrLowerer`, so the lowering makes an opaque exactly where the text has the declaration. Alternatives: a copy of the predicate in the lowering (the two drift, and a call the lowering keeps that the text does not cover is this bug again). Rule: 4. `BoundSerialiser` is in the fingerprint's folder; its text is unchanged, and every fingerprint test passes as it was.
- Decision: the IL lowering -> a method whose bound code has any operation of P2-149's rule, or a reference to storage of an explicit layout, is not lowered from IL (`IlFallback`, reason `il-layout`). Alternatives: the same two refusals inside `IlLowerer` (it has no bound tree, so "every type under the call" would be a second rule over ILAst; and its `sizeof`, pointer and inline-array fragments are fingerprinted by IL text, which holds no declaration either), nothing (the pairs this ticket makes Unknown are the ones the IL pass reads next, with the field map and the shared call again). Rule: 1.
- Decision: where the repro lives -> `tests/Equiv.Tests.Integration/LayoutEquivalenceTests.cs`, beside the lowering tests the ticket names. Alternatives: `IrLowererTests` alone (criterion 1 asks for an Equivalent from the solver, and the frontend's tests do not reference a solver). Rule: 5.
- The ticket's Files and Tests sections now name what the decisions above touch.

### Criterion 3: what changed
- `IrLowerer.Lower` asks `ReadsLayout` before it dispatches, so every read, assignment, compound
  assignment, increment, deconstruction and subscription is covered at one place; a target the
  graph captures ahead of a branching value is no longer kept as a map or a property place, so it
  is read as the opaque where it is captured.
- A field of a type with no explicit layout, with or without a `[StructLayout]`, lowers as it did
  (`AFieldOfATypeWithoutAnExplicitLayoutIsStillItsMap`), and no snapshot changed.
- A call into the two namespaces under which no type of the solution appears is the call it was
  (`ACallThatIsHandedNoDeclaredTypeIsStillACall`).
- Cost, seen with a throwaway probe and not kept as a test: `task.GetAwaiter().GetResult()` on a
  `Task<K>` for a class `K` of the solution, `ConditionalWeakTable<K, K>.Add` and
  `CollectionsMarshal.AsSpan(List<K>)` are now a `Layout` fragment with a fingerprint, shared
  beside one declaration. An interpolated string with a hole of a solution type was already an
  `InterpolatedString` fragment, and an `await` is still its call.

### Criterion 4: the corpus
- `powershell-19687` (net8.0 on both sides), compare mode quick, `--jobs 4`, one run per column on
  2026-10-09, exit 1 both. Before is `main` at `6a12ae27`; after is this branch at `327ecd7f`.
  199 s and 175 s of wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 34183 | 34183 |
  | EQ001 | 34149 | 34149 |
  | EQ002 | 6 | 6 |
  | EQ003 | 28 | 28 |
  | EQ004 / EQ005 / EQ006 | 0 / 0 / 0 | 0 / 0 / 0 |
  | `matchedPairs` | 34183 | 34183 |
  | `pairsCongruent` | 34146 | 34146 |
  | `changedPairs` | 37 | 37 |
  | `changedPairsWithoutOpaque` | 10 | 10 |
  | `pairsWholeBodyOpaque` | 663 | 663 |
  | `pairsWithoutOpaque` | 28309 | 28224 |
  | `opaqueByReason.Layout`, each side | 0 | 313 |

  Compared as a multiset of location, rule id, `proofMethod` and `unknownReason`: no result is
  only in one run. None of the 37 changed pairs gained an opaque. The 85 pairs that left
  `pairsWithoutOpaque` are congruent, so the solver never sees them.
- The other reasons that moved, each side: `InstanceReference` 255 to 235, `Conversion` 877 to
  869, `ref-argument` 357 to 351, `SizeOf` 2 to 0, `None` 47 to 45, `AddressOf` 6 to 5,
  `DelegateCreation` 119 to 118, `undefined` 69 to 68, `FlowCaptureReference` 2 to 3. A call that
  is now one `Layout` fragment takes its arguments with it, so an opaque under it is no longer
  counted by itself.
- The first attempt at the before run exited 4 after 14 s with the legacy solution not loaded: the
  three steps `tools/corpus/README.md` lists for this pair had not been done in a fresh worktree.
  That run is void and its directory was deleted; both runs above were made after them.
- Not run: compare mode thorough. The IL pass reads only pairs the first pass leaves Unknown, and
  the rule that keeps a method out of it is unit-tested (`AMethodThatReadsALayoutKeepsItsOperationLowering`).
- The scoreboard is not touched: no file under `docs/runs/` is added here.

### What is left
- P2-152: the text of a body that uses an auto-property or an event of an explicit layout.
- P2-151: a generic member of the solution that reads its type parameter's layout (unchanged).
