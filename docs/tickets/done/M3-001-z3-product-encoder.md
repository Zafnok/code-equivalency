# M3-001 Z3 product-program encoder for acyclic IR
Status: done (PR #78)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M1-002, M1-003

## Goal
`Equiv.Verify.Z3` implements `IVerificationBackend` for acyclic procedure pairs: encode
both procedures over shared inputs, ask Z3 whether any observable can differ, decode a
model into a readable counterexample, and replay it through `IrInterpreter` to confirm.
Loops are rejected here with `Unknown(loop)`; M3-002 adds the ladder on top of this encoder.

## Spec references
VERIFICATION-MODEL.md sections 1, 2, 5, 6, 7; ADR 0005; ADR 0014; ADR 0018; ARCHITECTURE.md
backend bullets.

## Design

Contract in `Equiv.Core` (M1-003 stubs it; this ticket finalises it):

```
public interface IVerificationBackend
{
    Verdict Verify(ProcedurePair pair, VerificationOptions options);   // options: timeout, bound, call identity map
}
```

Encoding (`ProductEncoder`), one `Z3.Context` and `Solver` per pair, both disposed:

- Sorts: `IrBool` -> `BoolSort`; `IrBitVec(n)` -> `BitVecSort(n)`; `IrSort(name)` -> one
  `UninterpretedSort` per name, shared by both sides; `IrMap(k, v)` -> `ArraySort`.
- Every SSA var becomes a constant named `old.<name>` or `new.<name>`. Parameters are
  shared (ADR 0021): the C# parameters by position, the synthesised inputs by name, one
  constant per shared input, and never two parameters of different types. A parameter
  present on only one side (a synthesised heap map the other side never names) is
  declared once as the shared input; the other side's final value of it *is* that input
  (ADR 0018).
- Instructions become definitional equalities asserted unconditionally: `t = bvadd(a, b)`,
  `t = select(m, k)`, `m2 = store(m, k, v)`. SSA makes this sound: a definition in an
  unreachable block constrains a name nothing reads. `IrOverflows` uses
  `MkBVAddNoOverflow`/`MkBVSubNoUnderflow`/`MkBVMulNoOverflow` and the signed variants,
  negated. `IrUnary` widths via `MkZeroExt`/`MkSignExt`/`MkExtract`.
- Control flow without unrolling: a Bool `reach.<side>.<block>` per block. Entry is true.
  `reach.B = OR over predecessors P of (reach.P AND edge(P -> B))` where `edge` is
  `true` for goto, `cond`/`not cond` for branch arms, the case equality for switch.
  `IrUnreachable` asserts `not reach.B`. Phi: `x = ite(reach.P1 AND edge(P1->B), v1, ite(...))`.
- Observables per side: `returned = OR reach of return blocks`; `ret = ite chain over
  return blocks`; `threw = OR reach of throw blocks`; `exceptionType` an `Int` constant
  per interned type name selected by ite chain; every `Ref`/`Out` parameter's final value
  likewise, from the exits' `outs`, over the union of both sides' by-ref parameter names.
  Heap maps are `Ref` parameters (ADR 0018), so this is how the final heap is compared;
  map equality is plain Z3 array equality. There is no special case for maps.
- Calls (mutual summaries, stateful per ADR 0018): per `CallIdentity` and signature, one
  `FuncDecl` `f_callee(args..., pos) -> result` and `f_callee_threw(args..., pos) -> Bool`,
  shared by both sides. `pos` is the call's position in its own side's trace: the number of
  calls that side executed before it. Encode it as a bv32 term, `cnt.<side>.<block>` at
  block entry (an ite over predecessors, like a phi, with entry = 0), plus 1 per earlier
  call in the block. Two calls on one side therefore never share a result, while an
  unchanged call sequence still does. Identities present in the config call-identity map are unified before lookup.
  Identities in the runtime-changes table (M2-006) get side-specific functions
  `f_callee_old` / `f_callee_new`, which is what makes them divergent.
- Call trace: a Z3 datatype `Value` with one constructor per IR type in use
  (`ofBool`, `ofBv32`, `ofSortX`, ...; constructors are injective, which is why a
  datatype and not a cast), a datatype `Event(callee: Int, args: Seq<Value>)`, and per
  side a `Seq<Event>` built as the concatenation, in reverse-postorder block order, of
  `ite(reach.B, <events of B in order>, empty)`. Trace equality is one `Seq` equality.
- Query: assert `NOT (returned_old == returned_new AND ret_old == ret_new AND
  threw_old == threw_new AND exceptionType_old == exceptionType_new AND
  outs equal AND trace_old == trace_new)`, restricted to inputs that reach no opaque
  (next bullet). `SATISFIABLE` -> Divergent; `UNSATISFIABLE` -> Equivalent unless an
  opaque is reachable; `UNKNOWN` -> Unknown(timeout) with the solver reason string.
- Opaque (ADR 0014): `opaque.<side> = OR reach.B` over blocks containing an `IrOpaque`.
  The query above is asserted together with `NOT opaque.old AND NOT opaque.new`;
  `SATISFIABLE` -> Divergent (the counterexample reaches no opaque, so replay is exact).
  If it is `UNSATISFIABLE`, check `opaque.old OR opaque.new`: `SATISFIABLE` ->
  `Unknown(opaque, reasons of the reachable opaques)`; `UNSATISFIABLE` -> Equivalent.
  There is no def-use short circuit: an opaque stands for effects the frontend dropped,
  not only for a value.

Counterexample (`ModelDecoder`): read parameter values from `solver.Model` with
`Eval(c, completion: true)`; render bitvectors as signed and unsigned decimals; sorts as
`#n` tokens; produce `Counterexample(inputs, oldOutcome, newOutcome, oldTrace, newTrace)`.
Then replay both procedures in `IrInterpreter` with a call oracle built from the model's
function interpretations (the model evaluated on `f(args, pos)`, which is `model.FuncInterp`'s
entries with `Else` for unlisted args), keyed by the position `IrInterpreter` now passes to
`ICallOracle.Answer`. If the
replay does not diverge, that is an encoder bug: fail loudly with both results in the
message; do not report Divergent.

## Deliverables
- [ ] `ProductEncoder`, `SortMapper`, `TraceEncoder`, `ModelDecoder`, `Z3Backend`,
      `VerificationOptions`. Z3 objects never escape `Equiv.Verify.Z3`.
- [ ] Unit tests per instruction kind: encoder emits the expected assertion shape
      (compare `Expr.ToString()` snapshots via Verify) and the expected verdict on a
      two-block fixture. Use IR text fixtures parsed with `IrText.Parse`.
- [ ] Fixtures for each observable: return, out param, throw vs no-throw, different
      exception type, same calls different order, extra call, runtime-changed callee.
- [ ] Opaque fixtures (ADR 0014): `opaque-void-effect`, `opaque-other-path`.
- [ ] Heap and call-state fixtures (ADR 0018): `heap-write-dropped`, `heap-one-sided`,
      `repeated-call`.
- [ ] Property (soundness, `Equiv.TestSupport` generators): `Verify(P, P)` is Equivalent
      for 200 generated acyclic P; `Verify(P, Mutate(P))` is never Equivalent; every
      Divergent replays to divergence in the interpreter.
- [ ] Timeout test with a deliberately hard bitvector multiplication fixture and a 50 ms
      timeout produces Unknown(timeout).
- [ ] `docs/adr/0002-dependencies.md` unchanged unless a package is needed (none expected).

## Acceptance criteria (all must hold; nothing beyond them)
1. `IVerificationBackend.Verify` signature is changed once, here, to
   `Verdict Verify(IrProcedure old, IrProcedure @new, VerificationOptions options)`;
   the M1-005 fakes and tests are updated in the same PR.
2. Any procedure containing a back edge returns `Unknown(UnknownReason.Loop, ...)`
   before any Z3 call; the M3-002 ladder replaces this branch.
3. The eight observable fixtures under Tests exist as IR text files under
   `tests/Equiv.Verify.Z3.Tests/Fixtures/` and produce the verdict named in each
   file's first comment line.
4. The soundness property passes 200 cases for `Verify(P, P)` and 200 for
   `Verify(P, Mutate(P))`; every Divergent in those runs replays to divergence in
   `IrInterpreter`.
5. A flagged (`RuntimeChanged`) call on both sides yields `Divergent` with the EQ006
   rule, using side-specific functions.
6. `Z3Backend` disposes its `Context` on every path (a test uses a wrapper counting
   disposals).
7. `Program.Main` passes `Z3Backend`. `CompareCommand.Create`/`Run` take a
   non-nullable `IVerificationBackend` again, and the `null` skip branch in
   `BuildResults`, along with `Compare_WithoutBackend_SkipsMatchedPairs`, is removed
   (ADR 0012).
8. Opaque semantics (ADR 0014): fixtures `opaque-void-effect.ir` (a void pair that differs
   only by an `IrOpaque` statement) gives `Unknown(opaque)`, and `opaque-other-path.ir` (a
   divergence on a path that reaches no opaque, next to a branch that does) gives
   Divergent whose replay reaches no `IrOpaque`. The soundness property's `Mutate` may
   insert an `IrOpaque`, and such a pair is never Equivalent.
9. The soundness property of criterion 4 is scoped to the encoder in its own XML doc
   comment and in the harness's test-class summary: it generates IR, so it is evidence
   about this encoder and the ladder, not about the C# frontend, and the two heap gaps of
   VERIFICATION-MODEL section 2 (ADR 0015; tickets P1-005 and P1-006) are outside it. No
   code changes for this criterion; do not attempt either gap here.
10. Final heap (ADR 0018). Final values of `Ref`/`Out` parameters are compared over the
    union of both sides' parameter names, and a name present on one side only is compared
    against the shared input. Fixtures: `heap-write-dropped.ir` (`field.C.x` is a `Ref`
    parameter; old writes it and returns, new just returns) is Divergent;
    `heap-one-sided.ir` (only old names `field.C.x`, and it writes back the value it
    read) is Equivalent.
11. Stateful calls (ADR 0018). Call functions take the position term of the Design
    section. Fixture `repeated-call.ir` (old: two calls to the same callee with the same
    argument, returning whether the results are equal; new: the same two calls, returning
    `true`) is Divergent, and its replay diverges.
12. `ICallOracle.Answer` gains an `int position` parameter (the number of calls the run
    has made before this one), `IrInterpreter` passes it, and `ICallOracle`'s XML doc
    says the oracle must be deterministic in (callee, arguments, position). Existing
    oracles in tests ignore the new parameter. No other `Equiv.Core` contract changes
    beyond criterion 1's.
13. The soundness property's `Mutate` can also drop or change an `IrMapWrite` on a `Ref`
    map, and duplicate an `IrCall` whose result feeds an observable; such pairs are never
    Equivalent.

## Size guard
Six source files in `src/Equiv.Verify.Z3/`. No abstraction over Z3 (no `ISolver`
interface): the backend is the abstraction.

## Pitfalls
- Z3 `Context` is not thread-safe; never share one across verifications. xUnit v3 runs
  test classes in parallel; a `Context` per test is fine.
- `Microsoft.Z3` 4.12.2 loads `libz3` from `runtimes/<rid>/native`; if the test host
  cannot find it, check the `RuntimeIdentifier`-less build copies it (it should).
- Set timeout with `solver.Set("timeout", (uint)ms)`, not a global param.
- Reverse-postorder is required for the trace concatenation to reflect execution order
  on any path; compute it once per procedure and reuse in M3-002.
- Keep the encoder pure over IR; nothing in it may know about Roslyn.
- Until M3-007 lands, the C# frontend still emits heap maps as `In`, so on real samples the
  heap comparison is inert. The fixtures above are hand-written IR and do not depend on it.

## Out of scope
Loops, unrolling, induction (M3-002). SARIF (M1-004 already owns it; this ticket returns
`Verdict` objects only).

## Notes
- Decision: `IVerificationBackend.Verify`'s parameters are named `oldBody`/`newBody`, not `old`/`@new`: CA1716 (warnings are errors) rejects a parameter named after the keyword `new` on an interface member. The names mirror `ProcedurePair.OldBody`/`NewBody`. Alternatives: `legacy`/`modern`, suppressing CA1716. Rule: 1.
- Decision: `UnknownReason` gains `Loop`, which criterion 2 names (`Unknown(UnknownReason.Loop, ...)`); VERIFICATION-MODEL section 6's EQ003 reason list is patched to include it. No other Core contract changes (criterion 12). Rule: 1.
- Decision (superseded in review by ADR 0021): parameters were shared by name. That proved a false Equivalent for two same-typed parameters whose names are swapped in the signature (callers bind by position), a false Divergent for a renamed parameter, and an `ArgumentException` for a synthesised input that changed type. `ProductEncoder.Pair` now shares the C# parameters by position and the synthesised inputs (`this`, dotted names) by name, and splits a pair of different types into one input per side: `in.<old name>`, or `in.new.<name>` for an input only the new side has. Fixtures `parameters-swapped` (Divergent), `parameter-renamed` (Equivalent), `heap-type-changed` (Divergent); `IrGen.Mutation` gains "swap parameters", and `SoundnessPropertyTests.RenamingTheParametersKeepsAProcedureEquivalent` runs 200 renamed pairs. Rule: ADR.
- Decision: a return type that differs between the sides (the matcher's identity does not include it) makes the return values agree only on inputs where neither side returns; equal return types compare through a shared `ret.none` default so two throwing runs agree. Alternatives: `ArgumentException` (would crash the CLI on a legitimate `int` to `long` change). Rule: 4.
- Decision: `CompareCommand.BuildResults` throws `InvalidOperationException` for a matched pair without a lowered body (a frontend bug; `CSharpFrontend` always attaches both), covered by `Compare_PairWithoutBodyIsAFrontendBug`. Alternatives: an Unknown verdict (needs an `UnknownReason` the spec does not define). Rule: 2.
- Decision: the replay oracle evaluates the model on the call function applied to the replayed arguments and position (`model.Eval(f(args, pos), completion: true)`) instead of walking `model.FuncInterp` entries itself. Z3 evaluates an application exactly as the interpretation's entries plus `Else` (and completes a function the model leaves out), so the answers are identical with less translation code. The Design sentence is patched. Alternatives: walk `FuncInterp.Entries` and match decoded arguments. Rule: 1.
- Decision: the counterexample is Core's existing `Counterexample(Inputs, Old, New)`, whose `IrRun`s already carry outcome, outs and trace; its inputs are the shared inputs in `ProductEncoder.Pair` order (the old side's parameters first), and each side's replay binds its own parameters through that pairing. Values are `IrValue`s, so an uninterpreted element is `sort "S" n` (a literal keeps its id, any other element gets the next free id) and a bitvector keeps its bits; `CounterexampleText` (Core, criterion 12) renders them as before, so no signed/unsigned rendering change lands here. Rule: 4.
- Decision: the `Value` datatype always has a Bool constructor, so it is never empty when no call has arguments. Rule: 5.
- Decision: `Side` and `ProductEncoding` are nested in `ProductEncoder`: Meziantou MA0048 wants one top-level type per file, and the size guard allows six files. `VerificationOptions` already lives in Core (the interface names it), so `src/Equiv.Verify.Z3/` has five source files. Rule: 4.
- Decision: the solver is a tactic pipeline, `solve-eqs`, `simplify`, `propagate-values`, `solve-eqs`, `smt`, with one fresh solver per query and no check-assumptions (see the first Note for why). Rule: 1.
- Decision: the timeout fixture is the 64-bit division identity `(a / b) * b + a % b` vs `a` (Equivalent, but the solver was still running after 20 s). A 64-bit semiprime factoring query was solved in 13 ms, and 64-bit distributivity is normalised away by `simplify`. Rule: 3.
- Decision: the soundness generators live in `Equiv.TestSupport` (`IrGen.AcyclicProcedure`, a `ref` heap map `field.Gen.x` with `Store`/`Load`, and the new `IrGen.Mutation` edits: insert an opaque, drop or change a map write, duplicate a call). `IrGen.Procedure` gets the heap too, so M3-002 inherits it; `Equiv.Core.Tests`' generator properties still pass (discard rate within bounds). Rule: 4.
- Decision: the Linux CI legs (`gates (ubuntu-latest)`, every `stryker` leg) take `libz3.so` from the PyPI `z3-solver==4.12.2.0` manylinux wheel, pinned by the SHA-256 PyPI publishes and checked by `pip --require-hashes`, and put it on `LD_LIBRARY_PATH`. No NuGet package changes, so `docs/adr/0002-dependencies.md` is unchanged per the deliverable, although its "ships win/linux/osx natives" is wrong (see below). Alternatives: Ubuntu's `libz3` (4.8.x, older than the 4.12.2 binding), the GitHub release zip (no published digest). Rule: 1.
- Note: `Microsoft.Z3` 4.12.2 ships `libz3` for `win-x64` and `osx-x64` only; there is no `runtimes/linux-x64`. ADR 0002's Microsoft.Z3 row is corrected in review, and M3-004's Notes carry the linux-x64 publish and Docker image gap. Only Windows was run locally; the Linux step was first run by this PR's CI (green).
- Note: Z3's default solver is unreliable for this encoding. After one query with check-assumptions (or `push`) it stays incremental and skips preprocessing, so the two sides' copies of an unchanged symbolic `udiv` are left as two circuits and `Verify(P, P)` timed out. The non-incremental default tactic timed out on a plain diamond. `solve-eqs` has to run before `simplify`: `simplify` rewrites `(= r (not x))` to `(not (= x r))`, which `solve-eqs` no longer reads as a definition, and a `reach` variable left behind again hides a copied multiplier. The tactic pipeline makes both sides one term, and the self-comparison folds to `false` in preprocessing.
- Note: `Microsoft.Z3.Constructor`'s finalizer calls `Z3_del_constructor` directly. If it runs after the `Context` is disposed, the test host dies with `0xC0000005`. `TraceEncoder` disposes its constructors right after `MkDatatypeSort` and keeps only the sort's constructor `FuncDecl`s.
- Note: with completion, Z3 4.12 evaluates a model array to a store chain over a constant array; no probe produced `as-array`, so the decoder handles only that shape, and any other shape fails loudly.
- Decision (review): `equiv.config.json`'s `suppressRuntimeChanges` is applied where calls are flagged: `IrLowerer.Lower` takes the list and `CallIdentityFactory` uses `RuntimeChangeTable`'s existing suppressing `TryMatch`, so a suppressed member is never `RuntimeChanged` and the backend needs no new input. Covered by `CallIdentityFactoryTests.ASuppressedMemberIsNotMarkedRuntimeChanged`. Alternatives: add the list to `VerificationOptions` (a Core contract change criterion 12 rules out). Rule: 4.
- Note: on the samples, the wired CLI now gives `identical` and `renamed-locals` EQ001, `removed-null-check` and `added-branch` EQ002, `loop-bound-change` EQ003 (Loop), and `added-removed`'s matched `Add` EQ001. `ComparePipelineTests.IdenticalYieldsNoResults` became `IdenticalYieldsOnlyEquivalentResults`, and the `AddedAndRemovedHaveLocations` snapshot gained the `Add` EQ001 result.
- Decision (review): a backend failure in `CompareCommand` is rethrown as `InvalidOperationException` naming the pair (`Compare_BackendFailureIsRethrownNamingThePair`); it still ends the run, as the Design's "fail loudly" asks. Whether a run should continue past a crashing pair (SARIF `toolExecutionNotifications`, a distinct exit code) changes the exit-code contract and is left to an ADR. Rule: 4.
- Decision (review): a solver `unknown` stays `UnknownReason.Timeout` (Core has no other reason for it), and the detail reads `solver returned unknown (<reason>) with a <n> ms timeout`, since the reason is not always a timeout. Rule: 4.
- Decision (review): `ModelDecoder` reads a map only as a store chain over a constant array and throws an "Encoder bug" `InvalidOperationException` naming the term for any other shape, instead of an index error. Rule: 4.
- Note (M3-027, 2026-09-24): the PyPI `z3-solver` Linux wheel workaround (the two notes above) is gone. `Microsoft.Z3` moved to 5.1.0 from the official Z3Prover/z3 GitHub release (ADR 0030), which ships `libz3` for linux-x64 itself.
