# ADR 0025: Floating-point, decimal and user-defined operators are shared pure functions

Status: accepted (2026-09-21)

## Context
VERIFICATION-MODEL section 2 makes `float`, `double` and `decimal` uninterpreted sorts with
equality only. The lowerer therefore makes every `Binary`, `Unary` and `Conversion` on them opaque
(coverage table rows `Binary`, `Unary`, `Conversion`), and the same goes for any user-defined
operator, including `string ==`. Business code in the migrations this tool targets is full of
`decimal` arithmetic. So a procedure that computes a price is Unknown even when its arithmetic is
unchanged. Modelling IEEE-754 in Z3 is on the post-MVP list, and `decimal` has no SMT theory at all.

## Decision
A new IR instruction, `IrPure(target, threw, function, args)`, applies a named function from a
closed catalogue in the frontend: `f32.add`, `f64.lt`, `dec.mul`, `conv.f64.i32`, and so on, plus
`op:<normalised operator identity>` for user-defined operators. The encoder declares one Z3 function
per name and shares it between both sides. An `IrPure` adds no trace event and does not depend on
the heap or on position.

- **Exceptions.** An operation that can throw has one Bool `threw` function per exception it can
  raise. For example, `dec.div` has `threw.divzero` and `threw.overflow`. Each branches to an
  `IrThrow` of that exception's exact type, so a `catch (OverflowException)` routes as it would at
  run time.
- **Runtime-sensitive functions.** Where behaviour differs between .NET Framework and .NET 10, the
  function is *side-specific* instead of shared: `old.f64.add` against `new.f64.add`. That covers
  floating-point to integer conversion (made saturating in .NET 9), and floating-point arithmetic
  when the legacy project runs on the 32-bit x87 JIT. It can never be proved equal.
- **Divergent verdicts.** A Divergent whose divergence depends on an `IrPure` result is decided by
  ADR 0026, never reported directly.

## Why
- Two sides applying the same deterministic operator to equal operands get equal results. A shared
  uninterpreted function proves exactly that, which covers the migration case: arithmetic that was
  unchanged, or moved without being altered.
- Pure functions need no position or heap argument, so they cost the solver less than a call and
  never create a trace mismatch just because a statement was reordered.
- Exact exception types keep `catch` routing sound. A single `ArithmeticException` edge would make
  `try { a * b } catch (OverflowException) { ... }` look identical to plain `a * b`. That would be a
  silent false Equivalent.
- Side-specific functions reuse the rule the runtime-changes table already applies to calls.

## Rejected
- **IEEE-754 floating point in Z3 now.** It is exact, but bit-blasting is slow and it does nothing
  for `decimal`. It stays post-MVP and could later replace the `f32.*` and `f64.*` functions behind
  the same instruction.
- **Lowering operators as `IrCall`s.** Sound, but each one becomes a trace event keyed by
  position, so reordering two independent computations looks like a divergence.
- **Adding axioms such as commutativity.** Quantified axioms make Z3 slower and less predictable.
  A rewrite that needs algebra is Unknown under ADR 0026, which is the honest answer.

## Consequences
- `decimal` and `double` code that did not change becomes provable. Code whose arithmetic was
  rewritten becomes Unknown(Abstraction) instead of Unknown(Opaque), which names the operator
  instead of a whole region.
- `IrInterpreter` must evaluate or taint `IrPure` (ADR 0026), and the IR validator, text format and
  CsCheck generators gain one instruction (the `equiv-extend-ir` skill).
- VERIFICATION-MODEL section 2 (instruction table, types paragraph) and section 5 changed in the PR that accepted this ADR.
- Ticket: M3-018. It depends on M3-016.
