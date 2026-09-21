# M3-017 An opaque fragment present on both sides is a shared call
Status: todo
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
