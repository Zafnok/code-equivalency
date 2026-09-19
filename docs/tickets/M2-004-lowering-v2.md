# M2-004 Lowering v2: loops, switch, try, null, maps
Status: todo
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
