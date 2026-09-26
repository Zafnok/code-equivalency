# M4-008 The remaining whole-body opaques: accessors, auto-properties, `catch` filters and bare `catch`
Status: done (PR #216)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-003, M3-010, M3-025

## Goal
ADR 0029 decision 3: every whole-body reason has an owner until `pairsWholeBodyOpaque` reaches zero.
M4-001 owns `foreach`, `using` and constructors, and M4-003 owns `lock`. This ticket owns the rest:
- **Arrow-bodied property and indexer accessors** (`int P => x;`). Roslyn gives these an
  `IBlockOperation`, not an `IMethodBodyOperation`, so today they are reason `Block`.
- **Auto-property accessors** (`{ get; set; }`). These have no body, so today they are reason
  `no-body`. They are enumerated as procedures, so every auto-property on a DTO is a method-scope
  Unknown.
- **`catch` with a `when` filter and a bare `catch`** (reason `catch-filter`).
- **Constructors M4-001 left whole-body** (ADR 0029 clarification, M3-025): one that omits its
  type's field initializers (reason `field-initializer`), a static constructor and a primary
  constructor with base arguments (reason `ConstructorBodyOperation`).

After this ticket none of these makes a whole body opaque.

## Spec references
ADR 0029; ADR 0014; M2-004's `Try` lowering (the first `catch` whose type the thrown type converts to);
VERIFICATION-MODEL sections 2 and 3; `docs/tickets/IOPERATION-COVERAGE.md`.

## Design
- Accessors: `ControlFlowGraph.Create` has an `IBlockOperation` overload. Lower that graph through
  the same path as a method body.
- Auto-property get and set: a read or write of the synthesised `field.<Type>.<BackingField>` map
  at the receiver, or at the type token when static, exactly as M2-004 lowers a field. Name the
  backing field from `IPropertySymbol` so both sides agree. Two auto-properties with the same name
  on the same type are then the same slice on both sides.
- Bare `catch`: a clause that every thrown type converts to. Place it by source order in the
  existing first-match rule.
- `when` filter: lower the filter's CFG region as a branch evaluated after the type test. False
  falls through to the next matching clause, or rethrows to the enclosing region. A filter that
  itself throws is treated as false, as .NET does; lower that as a branch on the filter's `threw`
  flags.

## Acceptance criteria (all must hold; nothing beyond them)
1. An arrow-bodied getter lowers without `IrOpaque` when its expression is otherwise lowerable. A
   snapshot pins one.
2. An auto-property getter reads, and its setter writes, the backing-field map. A C# method that
   sets and then gets the property is proved Equivalent to one that writes and reads a field named
   the same way, through the lowering oracle or a dedicated sample pair.
3. A bare `catch` catches every thrown type in source order.
4. A `when` filter that evaluates false passes the exception on. A filter that throws counts as
   false. Each has a snapshot and an interpreter test.
5. The reasons `Block`, `no-body`, `catch-filter`, `field-initializer` and `ConstructorBodyOperation`
   no longer appear for these constructs.
   `WholeBodyReasonOwners` (M3-025) drops them, and IOPERATION-COVERAGE's rows say `lowered`.
6. `business-layer`'s `pairsWholeBodyOpaque` does not rise, and it falls if the sample holds any of
   these constructs. Add one auto-property and one filtered `catch` to the sample if it holds
   neither.

## Files
- `src/Equiv.Frontend.CSharp/Lowering/*` (the collaborators P1-003 extracted)
- `docs/tickets/IOPERATION-COVERAGE.md`, `samples/business-layer/**` if criterion 6 needs it
- tests in `tests/Equiv.Frontend.CSharp.Tests`, the affected snapshots

## Tests
`ArrowAccessorLowersThroughTheBlockGraph`, `AutoPropertyGetReadsTheBackingFieldMap`,
`AutoPropertySetWritesTheBackingFieldMap`, `BareCatchCatchesEverything`,
`FalseFilterPassesTheExceptionOn`, `ThrowingFilterCountsAsFalse`, `NoWholeBodyReasonLeftForTheseConstructs`,
plus the lowering-oracle generator extended with accessors.

## Size guard
No change to IR records or the encoder. More than about eight production files means stop.

## Out of scope
`foreach`, `using` and constructors (M4-001). `lock` (M4-003). Exception filters that read the
exception object's members beyond its type (they stay expression-level opaque). `init` accessors
with `required` semantics.

## Notes
- Decision: a caller reads and writes an auto-property as its backing field's map when no override can replace
  it (not `virtual`, not `override`) and neither accessor has a body; otherwise it still calls the accessors
  (M3-010). Criterion 2 needs this: a call is uninterpreted, so a set-then-get through calls is never provably
  a field write-then-read. Compound and captured writes take the same path, so one property is never half
  inlined and half called.
- Decision: the backing field's map is `field.<Type>.<Property>`, named from `IFieldSymbol.AssociatedSymbol`, so
  the C# 14 `field` keyword and a bodiless accessor of the same property share it, and a field `P` on the other
  side is the same slice.
- Decision: a constructor is a sequence of CFGs (`ControlFlowGraph.Create` of each `IFieldInitializerOperation` /
  `IPropertyInitializerOperation`, then the constructor's), each exit going on to the next entry; flow captures
  and switch chains are per graph. Initializers of a partial type spread over files are ordered by file path,
  since C# leaves that order unspecified.
- Decision: a `when` filter is copied to each raise site, keyed by its true and false exits, like a `finally`
  copy. It runs before the finallys between the throw and its handler, as .NET's first pass does; an exception
  raised inside the copy goes to its false exit.
- Decision: a struct's auto-property accessor is an expression-level `InstanceReference` opaque, as a struct's
  `this` is everywhere else (M4-001).
- Decision: the lowering oracle keeps `P` as the auto-property (now a `field.Oracle.P` map, its final value
  compared) and adds `Q`, a property with accessor bodies, so M3-010's getter and setter calls stay exercised;
  `AccessorsAgreeWithCompiledCSharp` checks `get_P`, `set_P` and the arrow getter `R` against the compiled class.
- Deviation: `no-body` is not gone from the code. A record's primary constructor has no operation (the writes
  of its positional properties are in no operation tree), so it stays one whole-body opaque with reason `no-body`,
  as do `extern` and abstract members. No sample holds one, so `WholeBodyReasonOwners` drops the row as criterion
  5 says, but a corpus record would surface an unowned reason; it needs a follow-up ticket.
- business-layer: `pairsWholeBodyOpaque` 16 -> 2 (only `async` and `lock` left), `pairsWithoutOpaque` 8 -> 22; its
  SARIF loses the auto-property getters from `assumedCallees`, verdicts unchanged. The sample already held
  auto-properties, so criterion 6 needed no sample change.
- Toolchain: in a fresh worktree `webapi-basic` fails to load until the samples are restored (`build.ps1`'s
  "restore samples" step: MSBuild `-t:Restore` for legacy, `dotnet restore` for modern).
