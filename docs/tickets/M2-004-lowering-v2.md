# M2-004 Lowering v2: loops, switch, try, null, maps
Status: in-progress
Effort: M
Model: Opus, medium effort (extends the M2-003 SSA builder to loops, try regions and heap maps). Sonnet only at high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-003

## Goal
Extend `IrLowerer` from M2-003 to the constructs the samples need: loops as the CFG
presents them, `switch`, `throw` and `try` regions, null checks on reference-typed
locals, and field and array access as SSA maps. Each construct is one commit with its
own snapshot, following the `equiv-extend-ir` skill. The IOperation coverage table
grows accordingly.

## Spec references
VERIFICATION-MODEL.md sections 2 and 3; `equiv-extend-ir` skill; M2-003 Design.

## Acceptance criteria (all must hold; nothing beyond them)
1. Loops: the CFG has no loop constructs, only back edges. The SSA builder must handle
   them (Braun et al. incomplete phis + sealing). `while`, `for`, `do`, `foreach` over
   arrays lower without `IrOpaque`. `foreach` over anything else is `IrOpaque("foreach-enumerator")`.
2. `switch` on integral and bool scrutinee lowers to `IrSwitch`; pattern switches beyond
   constant patterns are `IrOpaque("switch-pattern")`.
3. `throw new T(...)` lowers to `IrThrow("T")` with the constructor call recorded as an
   `IrCall` first. `throw;` (rethrow) is `IrOpaque("rethrow")`.
4. `try`/`catch`/`finally`: a `try` region whose `catch` clauses catch a specific type
   lowers to: body blocks; every `IrThrow(T)` inside the body whose `T` equals or derives
   from a catch type (Roslyn `Conversion.IsImplicit` on the type symbols) becomes an
   `IrGoto` to that catch block; `finally` blocks are duplicated onto every exit path
   (normal, return, throw). `catch` without a type or `when` filters are
   `IrOpaque("catch-filter")`. Throws from `IrCall.Threw` inside a `try` also route to
   matching catches (a call's unknown exception type matches any catch, conservatively
   as `IrOpaque("call-throw-in-try")` when more than one catch could apply).
5. Null: a reference-typed local or parameter `x` gets a paired Bool shadow `x.isNull`;
   `x == null` compares the shadow; `x.Member` or `x[i]` on a shadow that is not proven
   false emits `IrBranch(x.isNull, throwBlock(NullReferenceException), continue)`.
   Parameters start with an unconstrained shadow; `new` expressions set it false.
6. Fields: `IFieldReferenceOperation` on instance fields lowers to `IrMapRead/Write` on
   a per-field map var named `field:<Ns.Type.Field>` keyed by the receiver `Sort` var;
   static fields are a map keyed by a constant `Sort` token. Arrays: `IArrayElementReferenceOperation`
   is a map keyed by the bv32 index with a separate `length:<arr>` bv32 var; index
   out of range emits a branch to `IrThrow("System.IndexOutOfRangeException")`.
7. Every construct above has a snapshot test and an IOPERATION-COVERAGE row; the
   lowering oracle generator gains `while` loops with a bounded counter and `if` on
   null-checked parameters.
8. All five M1-001 samples lower with zero `IrOpaque` nodes in methods the sample README
   marks as expected-Equivalent or expected-Divergent (a test asserts this).
9. Compound assignment (`+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`) and
   `++`/`--` (prefix and postfix) on integral locals and parameters lower as read,
   operate, write, with the same overflow, divide-by-zero and shift rules as the binary
   operator and the implicit narrowing back to the target type. Other targets stay
   `IrOpaque` by kind. Snapshot tests, IOPERATION-COVERAGE rows, and the oracle generator
   gains `+=` and `++` (ADR 0014; AC8 already needs them for `loop-bound-change`).

## Files
Additions inside `src/Equiv.Frontend.CSharp/Lowering/` only; no changes to `Equiv.Core`
except new `IrDiagnosticIds` if the validator needs one.

## Tests
One snapshot per construct (at least 10), oracle property extended, `Samples_LowerWithoutOpaque`.

## Size guard
If a construct needs more than about 150 lines of lowering code, mark it `IrOpaque`
with a reason and file a post-MVP ticket instead.

## Out of scope
Strings as anything but `Sort`. `using`, `lock`, `async`, iterators, LINQ, delegates,
generics-specific behaviour (all `IrOpaque` with a reason).

## Notes
- Decision: compound assignment and `++`/`--` -> one `Update` helper that reads the target, resizes it to the C# promoted operator type (`TypeMapper.Promote`), reuses the binary-operator path (so the overflow, divide-by-zero and `MinValue / -1` edges are identical), resizes back to the target type and, when checked, throws if the narrowing loses the value. The shift operator's type comes from the promoted target; every other operator's from `compound.Value.Type`, which Roslyn has already converted to it. Alternatives: re-implementing C# binary numeric promotion for both operands. Rule: 1.
- Decision: loops -> drop the whole-body opaque; the Roslyn CFG presents `while`, `for` and `do` as plain branches with a back edge, which the M2-003 SSA builder already handles (it was written for `goto` loops). No lowering code was needed at all. Alternatives: a loop-shaped IR node. Rule: 4.
- Decision: `foreach` -> whole-body `IrOpaque("foreach-enumerator")` for every collection, arrays included, and ticket P1-003 for the array case. Acceptance criterion 1 asked for `foreach` over arrays to lower, but `ControlFlowGraph.Create` desugars an array `foreach` into `IEnumerable.GetEnumerator()`, a `Try`/`Finally` region around `MoveNext()`, and `IEnumerator.Current` as a property reference on an uninterpreted receiver; the CFG never shows an index. Rewriting that six-block shape into an index loop is well past this ticket's size guard, which says to mark the construct opaque and file a post-MVP ticket. Alternatives: pattern-match the enumerator shape here (over the size guard). Rule: the ticket's Size guard.
- Decision: `IrThrow`'s exception type -> `TypeMapper.MetadataName` of the created object's static type (`System.ArgumentException`, `C+E` for a nested type), not a renamed procedure identity. It is a type name, and the shared throw blocks M2-003 already emits are spelled the same way. Alternatives: the rename map's type identity. Rule: 1.
- Decision: `new T(...)` -> an `IrCall` to the constructor with a `Sort` result and a threw edge, everywhere, not only under `throw`. Acceptance criterion 5 needs `new` to set a null shadow to false, which needs a value. `IObjectCreationOperation.Initializer` is always null in a CFG (the CFG flattens an object initializer into a capture plus assignments) and `Constructor` is non-null even for a struct's synthesised parameterless one, so neither needs a guard; `new T()` on a type parameter is a different operation kind and stays opaque. Alternatives: lower it only under `throw`. Rule: 4.
- Decision: `switch` -> a CFG-level fold, `SwitchChains`. Roslyn's CFG contains no switch at all: a `switch` statement is a chain of `Binary Equals` branches on one captured scrutinee and a `switch` expression a chain of `IsPattern` constant-pattern branches, so the construct has to be recognised and folded back into one `IrSwitch`. A block joins a chain only when it has no instructions of its own, exactly one predecessor, both successors Regular, and a constant of the scrutinee's own bitvector or Bool type; a chain needs two tests, so `if (x == 1)` stays a branch. Alternatives: reading `ISwitchOperation` from the operation tree (it names no CFG blocks), leaving the chain as branches (acceptance criterion 2 asks for `IrSwitch`). Rule: 3.
- Decision: pattern tests -> `IIsPatternOperation` lowers per pattern (constant -> equality, discard -> `true`, everything else -> `IrOpaque("switch-pattern")`) rather than the whole body becoming opaque when a switch has a non-constant case. A `when` guard is an ordinary branch after the constant test, so a guarded constant arm still lowers. Alternatives: whole-body opaque, as M2-003 did for `switch`. Rule: 4.
- Note: `SsaBuilder` had no `IrSwitch` case in `Successors` or the operand rewrite, so the first folded switch produced IR that did not validate. Both are there now.
- Decision: the null shadow's source -> a `null.<Sort>` map input read with `mapread`, not an extra Bool parameter per reference parameter. The IR has no way to spell "an unconstrained Bool" except `IrOpaque`, which would make every body with a reference parameter Unknown (ADR 0014), and a free variable that is not a parameter is not shared between the two sides by the M3-001 product encoding. A map keyed by the reference makes nullness a function of the value, so equal references are equally null and a call result or a converted value has a shadow too. Alternatives: a Bool parameter per reference variable (two equal references could then differ in nullness), an `IrOpaque` shadow. Rule: 1.
- Decision: synthesised inputs (`this`, `null.<Sort>`, and the field and array maps of acceptance criterion 6) -> `In` parameters appended to the C# parameters, ordered by name. VERIFICATION-MODEL.md section 2 now says so. Acceptance criterion 6 spells them `field:<Ns.Type.Field>` and `length:<arr>`, but the IR text format's variable names take only letters, digits, `_`, `.` and `$` (M1-002), and this ticket may not change `Equiv.Core`, so the separator is a dot and any other character becomes `_`. Alternatives: extending the IR lexer (a Core change this ticket's Files section forbids). Rule: 1.
- Decision: a constant of an uninterpreted sort (a string, a floating-point value, an enum member, `null`) -> `IrConst` of a designated element of that sort, chosen by an FNV-1a hash of the constant's invariant text, with `null` reserved as element 0. Equal constants are then the same element on both sides, which acceptance criterion 8 needs for `"Hello, "`, and strings stay uninterpreted as the Out of scope section requires. Alternatives: `IrOpaque` per literal (acceptance criterion 8 fails). Rule: 3.
- Decision: a field's map -> keyed by the receiver value for both reference and value types; a static field's by element 0 of its declaring type's sort, which is safe because only that field's own map is keyed by it. Keying a value type's field by the struct value gives value semantics for free. An array's map is per array *variable* (`array.<v>`, `length.<v>`), as acceptance criterion 6 says, so two variables holding the same array are two slices; several dimensions, a long index or an array that is not a plain variable stay opaque. Alternatives: one map per element type keyed by (array, index), which models aliasing but is not what the ticket describes. Rule: the ticket's acceptance criterion 6.
- Decision: `a.Length` on an array variable -> that variable's `length.<v>` var, the same one the bounds check uses. It is the only property this ticket lowers. Alternatives: leaving it opaque, which would make every bounds-checked loop opaque. Rule: 3.
- Note: C# evaluates an array assignment's receiver and index, then the value, and only then stores, so the bounds check is emitted after the value in an assignment and before the read in a load. Getting this backwards changes which exception a statement throws, which is observable.
- Decision: `finally` -> `IrLowerer.Copy(region, continuation)` re-lowers the region's CFG blocks into a fresh block map whose structured-exception-handling exit jumps to the continuation, memoised per continuation. `finally` blocks are excluded from the main pass, so they exist only as copies. Roslyn's `ControlFlowBranch.FinallyRegions` already names, in execution order, the finallys a normal or return branch leaves, so only the exception edges need `ExceptionRegions.Route`. A return evaluates its value first, then unwinds, which is what C# does. Alternatives: a single shared finally with a "where to go next" variable (needs a switch on a synthetic token and defeats the SSA naming). Rule: 1.
- Decision: a `switch` chain is only folded when both of each test's successors cross no `finally` (`FinallyRegions.IsEmpty`). A `catch` does not affect a normal edge, so that is the whole condition. Alternatives: teaching `SwitchChains` about regions. Rule: 4.
- Decision: `using` and `lock` -> whole-body `IrOpaque("using")` / `IrOpaque("lock")`. The CFG gives them ordinary `Try`/`Finally` regions, which this ticket now lowers, so without an explicit guard they would have started lowering even though the Out of scope section excludes them. Alternatives: letting them lower (out of scope, and `Dispose` on an interface receiver is an opaque call whose ordering we have not thought through). Rule: the ticket's Out of scope section.
- Note: `throw new T(...)` inside a `try` with more than one `catch` still produces one `IrOpaque("call-throw-in-try")`, because the constructor call's own `threw` flag has no known type and acceptance criterion 4 says that is opaque when several catches could apply. Tests that need an opaque-free `try` therefore raise through `/` or `checked` instead.
- Note: a `catch` block no exception edge reaches is dropped by the SSA builder as unreachable, so a test for `throw;` needs a `try` body that can actually throw.
- Decision: `string + string` -> an `IrCall` to `System.String::Concat(string,string)`, whose identity is built with the same `ProcedureIdentityNormalizer.Member` the frontend uses everywhere, so the rename map applies and both sides agree. Strings stay uninterpreted, which is what the Out of scope section requires, and acceptance criterion 8 needs it: `removed-null-check` is `"Hello, " + name.ToUpper()`. Alternatives: looking the symbol up in the compilation (a branch no test can reach when it fails), leaving it opaque (acceptance criterion 8 fails). Rule: 1.
- Decision: the oracle's IR arguments -> built from `procedure.Parameters` by name, not positionally, because a synthesised heap input is only a parameter when the body needs one. The generated methods gained a `string s` parameter for the null tests; every loop counts to a literal bound, so every generated method still terminates. Alternatives: always emitting every heap input. Rule: 4.
