# M4-004 An opaque fragment present on both sides is a shared call
Status: done (PR #222)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-015, M3-016, P1-005

## Goal
ADR 0024 decision 2. A method that changed only somewhere *other than* its lambda, LINQ chain or
interpolated string is today Unknown on every path through that construct. After this ticket, an
unlowerable fragment whose bound fingerprint occurs on both sides is encoded as one opaque call,
`opaque:<fingerprint>`, over the variables it reads, the heap and its position. Paths through it
are then decided. A divergence that depends on it is Unknown(Abstraction), by M3-016.

## Spec references
ADR 0024; ADR 0014; ADR 0018; ADR 0026; VERIFICATION-MODEL sections 2 and 5; the `equiv-extend-ir`
skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrOpaque` gains `string? Fingerprint` and `ImmutableArray<IrVar> Reads`. The reads are its
   `Uses()`. The text format, validator, `IrEquality` and generators are updated.
2. The lowerer fills both in for an expression-level opaque. Reads are the locals and parameters
   that the fragment's `IOperation` subtree reads, in first-occurrence order. It leaves
   `Fingerprint` null when any of these hold:
   - the fragment assigns a local or parameter other than its own result;
   - it contains a lambda or local function that captures a local assigned anywhere after the
     fragment in the CFG;
   - it is runtime-sensitive (M3-015's rule);
   - it is a whole-body opaque. Congruence covers that case.
3. `ProductEncoder` collects fingerprints present on both sides of the pair. For those, it
   encodes each occurrence exactly as an `IrCall` with identity `opaque:<fingerprint>`, arguments
   `Reads`, a heap in and out as P1-005 defines, a trace event, and a `threw` edge to
   `System.Exception`. All other `IrOpaque` keep ADR 0014's encoding.
4. The replay call oracle answers `opaque:` identities from the model, and M3-016's taint
   predicate marks them.
5. `IOPERATION-COVERAGE.md` says, on every `opaque` row, whether the fragment can be shared.
6. On `business-layer`, the method whose unchanged LINQ chain sits next to a changed integer
   branch is decided: Divergent on the integer path, or Equivalent. README and census updated.
7. Soundness property (VERIFICATION-MODEL section 7): for generated pairs that differ only
   outside a shared fragment, and for mutants inside one, the result is never Equivalent when the
   compiled programs differ on the oracle's inputs.

## Files
`src/Equiv.Core/Ir/IrOpaque.cs` and the text format, validator and equality,
`src/Equiv.Frontend.CSharp/Lowering/*`, `src/Equiv.Verify.Z3/ProductEncoder.cs`,
`src/Equiv.Verify.Z3/ModelDecoder.cs`, `tests/Equiv.TestSupport/*`,
`docs/tickets/IOPERATION-COVERAGE.md`, `samples/business-layer/README.md`.

## Tests
`ExpressionOpaqueCarriesFingerprintAndReads`, `FragmentAssigningALocalHasNoFingerprint`,
`LambdaCapturingALaterWrittenLocalHasNoFingerprint`, `RuntimeSensitiveFragmentHasNoFingerprint`,
`FragmentOnBothSidesIsASharedCall`, `FragmentOnOneSideStaysOpaque`,
`ReorderedSharedFragmentIsUnknownAbstraction`, the text-format round trip, and the property in
criterion 7.

## Size guard
No second call encoding: reuse `IrCall`'s path. If the encoder change exceeds about 120 lines,
stop.

## Out of scope
Fragments that write locals (they would need multiple outputs). Iterators and whole-body
constructs.

## Notes
- Deviation: `IrOpaque` gains `Threw` and `Heap` (P1-005's `IrHeapPair`s) besides criterion 1's `Fingerprint` and `Reads`.
  Criterion 3's `threw` edge and heap in and out need a flag the frontend branches on (inside a `try` it must reach the
  right handler) and SSA versions of each heap map before and after the fragment, which only the lowerer can place. They
  are `init` properties, empty by default, set only on a fingerprinted fragment, so every existing dump is unchanged.
- Decision: encoding -> `ProductEncoder.ShareFragments` rewrites each `IrOpaque` whose fingerprint is on both sides into
  `IrCall(target, threw, opaque:<fingerprint>, reads) { Heap = heap }` before the ladder runs (`LoopLadder.Verify` and
  `Independently`), so the unroller, fragmenter, encoder, replay oracle and taint predicate all see an ordinary call and
  there is no second call encoding (size guard: about 60 lines in `Equiv.Verify.Z3`). Alternatives: a case in
  `SideEncoder` (the replay would then need the interpreter to treat an opaque as a call). Rule: 4.
- Decision: an `Unknown(Abstraction)` that depends on an `opaque:` call points at the fragment: `LoopLadder.Verify` gives
  each such abstraction the span of the first occurrence of its fingerprint on its side, and the causes follow. M3-016
  left `Abstraction.Span` null until this ticket. Rule: ADR 0027 decision 4.
- Decision: the reads -> each local or parameter declared outside the fragment's syntax that the fragment references
  (through a lambda's body too), in pre-order first occurrence, each reference type's null shadow right after its value.
  The shadow is a separate SSA variable (`new` sets it to false), so the value alone does not determine nullness.
  "Outside the syntax" rather than "contained in the method" because a constructor's initializer graphs declare locals
  whose containing symbol is not the constructor. Rule: 1.
- Decision: a fragment with no fingerprint has no reads either: nothing uses them, and criterion 2's exclusions include
  captures of variables that are not yet stored. Rule: 3.
- Decision: the text -> `BoundSerialiser.SerialiseFragment`: a `Fragment` header, then M3-015's per-operation lines for the
  fragment's graph operation, each `IFlowAnonymousFunctionOperation` (which has no body in a graph) replaced by its lambda
  graph's `OriginalOperation`, and the method's parameters numbered by first occurrence like locals, since the fragment
  is called with its reads in that order. The hash is SHA-256 hex, as the body fingerprint's. Rule: 3.
- Decision: "assigned anywhere after the fragment in the CFG" is checked on the IR drafts in `SsaBuilder.Build`: a store
  of a captured variable later in the fragment's block or in any block reachable from it (the block itself when a loop
  leads back). Exception edges are explicit there, `finally` copies included. A write inside any lambda or local function
  of the graph (Roslyn data flow) also counts, since it can run at any time. Rule: 1.
- Decision: exclusions beyond criterion 2, each needed for "a function of the reads, the heap and the position": the
  fragment is not an expression (a `case` pattern); it holds a flow capture, null test or caught exception of the graph
  or a struct's `this` (values computed outside it); it calls or converts a local function declared outside it (whose
  body and captures are not in the fingerprint); it reads a `ref` local (a stale SSA value of an alias); or it reads or
  captures a variable the lowering does not track (a primary-constructor parameter). Rule: ADR 0024 decision 2.
- Decision: criterion 6's method is new, `OrderService.CappedLineCount`: no existing business-layer method had a LINQ
  chain beside a changed integer branch. It is Equivalent (bounded), not Divergent on the integer path; see the surprise
  below. Rule: 4.
- Decision: criterion 7's property is `SharedFragmentTests.AFragmentPairIsNeverEquivalentWhenItsProgramsDiffer` in
  `Equiv.Tests.Integration` over `PairGen.FragmentPair` (a generated method plus
  `x = ((System.Func<int, int>)(v => v op e))(e');`, then one `SyntaxMutator` operator, which lands outside or inside the
  lambda): 100 pairs, 20 inputs each, fixed seed, and it asserts some pair shared a fragment and was Equivalent. Locally
  it also passed 500 pairs on two other seeds. `IrGen` generates fragments too, so the IR soundness harness covers the
  encoding. Rule: 3.
- Decision: `SoundnessPropertyTests.LineScopedResidualClaimHolds` replays the pair `ShareFragments` returns: a shared
  fragment is a call, not a listed cause, so replaying the original would stop at it. Rule: 1.
- Surprise: every path past a shared fragment branches on its `threw` flag, which the replay taints (M3-016), so a real
  divergence after a shared fragment is `Unknown(Abstraction)`, never Divergent. Only a divergence on a path that skips
  the fragment is Divergent (`SharedFragmentTests.ADivergenceBesideASharedFragmentIsDivergent` in `Equiv.Verify.Z3.Tests`).
  That costs precision only; untainting a flag both sides compute from equal inputs would recover it.
- Surprise: `Equiv.Corpus.Seeder`'s `RenameLocals` does not rename inside a lambda, so a generated lambda that reads a
  local makes an uncompilable mutant. `FragmentMethod`'s lambda reads only parameters, the field and `u[0]`; the lowering
  tests cover lambdas that capture locals.
- Surprise: Roslyn's `DataFlowAnalysis.Succeeded` is false only for a region no bound node spans (a type inside a cast);
  every operation's own syntax, lambdas and local-function statements analyse, so there is no failure branch.
- Only one existing IR snapshot moved: `NullConditionalLengthWithFallback`'s `default(int?)` opaque is now a fragment.
