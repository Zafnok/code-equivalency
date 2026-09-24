# M3-007 Synthesised inputs: heap maps are `Ref`, and the naming rule is enforced
Status: done (PR #161)
Effort: L
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004, M3-001

## Goal
Two fixes to the synthesised inputs the lowerer adds to every procedure. They touch the same
`HeapInputs`/`Signature` code and the same samples-wide lowering check, so they land together.
(This ticket absorbed M3-012 in the 2026-09-21 consolidation.)

1. **Heap as output (ADR 0018).** `void Set(int v) { this.x = v; }` stops being Equivalent to
   `void Set(int v) { }`. The synthesised `field.*` and `array.*` map parameters become
   `IrParameterKind.Ref`, so every exit's `outs` names each map's final SSA version. The final heap
   then becomes an observable through the by-ref comparison M3-001 already performs. That is the
   frontend half of ADR 0018's "heap as output". The encoder half (a `Ref` parameter present on
   only one side is compared against the shared input) is M3-001 criterion 10.
2. **Naming rule (ADR 0021).** The product encoding tells a C# parameter from a synthesised input
   by name: a synthesised input is `this` or contains a dot, and a C# parameter never is. M3-001
   relies on the rule, but nothing checks that the frontend keeps it, and it already breaks once:
   an instance method with a parameter named `@this` (Roslyn reports its name as `this`) lowers to
   two parameters both named `this`, one the C# argument and one the receiver. The Debug build
   trips `Debug.Assert(IrValidator.Validate(...))` in `IrLowerer`. A Release build emits the
   invalid IR silently, and the encoder would share the argument by name instead of by position.
   Fix the collision and pin the rule with a test.

## Spec references
VERIFICATION-MODEL.md sections 1 and 2 (the synthesised-inputs paragraph); ADR 0018; ADR 0021;
M1-002's `outs` rule (`IrReturn`/`IrThrow` list every by-ref parameter once, in declaration order).

## Design
Do the two halves in order, each with its own commit: the `Ref` kinds and exit completion first
(criteria 1 to 6), then the naming fix and the samples-wide rule (criteria 7 to 10). The
samples-wide test in criterion 9 then checks the final parameter kinds and order too.

## Acceptance criteria (all must hold; nothing beyond them)
1. `HeapInputs.Parameters` returns `field.*` and `array.*` inputs as `IrParameterKind.Ref`, and
   `this`, `null.*` and `length.*` as `In`. Ordering is unchanged (by name). `HeapInputs`'s XML
   doc says which kind each input is, and why.
2. Every `IrReturn` and `IrThrow` the lowerer emits lists, for each `Ref` heap map, the SSA
   version live at that exit. On a path that never writes the map, that version is the map's
   input. `IrValidator` reports zero diagnostics on every lowered sample and on every snapshot
   fixture (it already enforces the `outs` rule).
3. Heap maps are created lazily, when first touched, so an exit lowered before a map existed
   still gets the map's out. Exits are completed after the whole body is lowered. Test
   `ExitsLoweredBeforeAFieldIsTouchedStillNameItsFinalVersion`: an early `return` precedes the
   first field write in source order, and that `return`'s outs name the field map's input.
4. `IrInterpreter` results for `Ref` heap maps appear in the run's outs, just as C# `ref`
   parameters' do. The lowering oracle compares them when the generated method writes a
   static field, so `LoweringOracleGen` gains a case that writes a static `int` field and
   returns nothing.
5. Every `.verified.txt` snapshot that touches a field or array is re-approved. The diff in each
   is only the parameter kind and the added outs.
6. VERIFICATION-MODEL section 2 already states the target model (ADR 0018). Check that it
   matches what was built; if it does not, correct the code, not the spec.
7. `class C { int f; int M(int @this) => f + @this; }` lowers to IR that validates, whose C#
   parameter is not a synthesised name, and whose receiver input is still `this`. `Z3Backend`
   gives Equivalent for it against the same method with the parameter renamed (`int M(int v) => f + v;`).
8. The predicate "is this parameter name synthesised" has one definition. If the frontend's
   test needs it, it moves from `Equiv.Verify.Z3.ProductEncoder.IsSynthesised` to `Equiv.Core`
   (next to the IR types) and the encoder calls it there. No copy lives in a test.
9. A frontend test lowers every method of every `samples/` pair (both sides) and asserts, for
   every procedure: every C# parameter is not synthesised, every synthesised input is, all
   C# parameters come before all synthesised inputs, and every `field.*`/`array.*` input is `Ref`.
10. VERIFICATION-MODEL section 2 states how a C# parameter whose name would collide is spelled
    in IR.

## Files
- `src/Equiv.Frontend.CSharp/Lowering/HeapInputs.cs`
- `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (exit completion; `Signature`)
- `src/Equiv.Verify.Z3/ProductEncoder.cs` and one `src/Equiv.Core/Ir/` file, only if criterion 8
  moves the predicate
- `tests/Equiv.Frontend.CSharp.Tests/Lowering/*` (new tests, re-approved snapshots, the
  collision and the samples-wide rule)
- `tests/Equiv.TestSupport/LoweringOracleGen.cs`
- `docs/VERIFICATION-MODEL.md`

## Tests
- `ExitsLoweredBeforeAFieldIsTouchedStillNameItsFinalVersion`, `HeapMapsAreRefAndNullAndLengthAreIn`.
- The extended lowering oracle, and the re-approved snapshots.
- Unit: the `@this` method from criterion 7 validates and keeps the two inputs distinct.
- Unit: the renamed-pair Equivalent from criterion 7.
- Integration-style unit over `samples/`: criterion 9.

## Size guard
No new `src/` file, except the one `Equiv.Core/Ir/` file criterion 8 may need. Any other
`Equiv.Core` change means stop: `Ref` parameters and `outs` already exist. If this ticket touches
the encoder's pairing logic, it has drifted: ADR 0021 already decided that.

## Out of scope
- The encoder's one-sided `Ref` comparison (M3-001). Heap effects of calls (P1-005). Array
  keying (P1-006). Treating `null.*` as writable.
- Replacing the naming rule with an explicit flag on `IrParameter` (rejected in ADR 0021).
- Turning `IrLowerer`'s `Debug.Assert` on the validator into a Release-mode check. Worth doing,
  but it changes what a lowering bug does to a run, which is ADR 0023's question.
- A Java frontend.

## Notes
- Decision: a C# parameter whose name would be synthesised (only `@this`, which Roslyn names `this`) is spelled `$this` in IR, with source name `this`. No C# identifier contains `$`, and the frontend's temporaries are `$<digits>`, so the name is never another variable's. Alternatives: `this_` (a real parameter can have that name), `@this` (`@` is not an IR name character). Rule: 4.
- Decision: the predicate moves to a new `Equiv.Core.Ir.IrParameterNames` (public, with the `Receiver` constant), and `IrUnroller`'s private copy of it is removed too, since criterion 8 asks for one definition and that copy was the second. `ProductEncoder.IsSynthesised`'s theory test moves to `Equiv.Core.Tests`. Alternatives: keep `ProductEncoder.IsSynthesised` as a forwarder (two names for one rule). Rule: 4.
- Decision: the criterion 7 Equivalent test and the criterion 9 samples-wide test live in `Equiv.Tests.Integration` (`SynthesisedInputNamingTests`), not `Equiv.Frontend.CSharp.Tests`: the first needs `Z3Backend`, the second needs the MSBuild loader over `samples/`, and only the integration project references both (it already has `InternalsVisibleTo` on the frontend). The `@this` validate-and-distinct unit test is in `IrLowererTests`. Rule: 1.
- Decision: the oracle's static field `F` starts each run at input `A`, and every run (not only void methods) compares `F`'s final value, read from the run's one out when the body touches the field. A field write's value is one variable and a literal, so it never branches: a branching value makes the CFG capture the field reference, and assigning through that capture is opaque (`FlowCaptureReference`, ticket P2-006). The first oracle run found this. `OracleMethod.Keyword` gains `void`. Rule: 3.
- Note: exit completion needed no new machinery. `SsaBuilder.Build` already fills every exit's outs after the whole body is lowered, and a lazily created slice is stored in the entry block, so the heap outs are appended to the list passed to `Build`.
- Note: the `ArrayElements` snapshot also gains two phis. The throw blocks there are reached both before and after the element write, so the version their outs name is a merge. That is the added outs' SSA, not a behaviour change.
- Note: locally, `samples/webapi-basic/legacy` does not load in this worktree (its Web API packages are not restored), so `EveryProcedureOfEverySampleKeepsTheNamingRule(webapi-basic)` fails here in the same way `EndpointDiscoverySampleTests` does on `main`. CI restores the packages.
