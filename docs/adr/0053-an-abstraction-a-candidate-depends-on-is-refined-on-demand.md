# ADR 0053: An interpretable pure function a candidate counterexample depends on is given its real meaning, and the pair is asked again

Status: accepted (2026-10-07). Narrows ADR 0025's rejection of "IEEE-754 floating point in Z3 now": it stays
rejected for every pair from the start, and is adopted for a pair whose candidate needs it. Adds a case to ADR
0026's taint rule and names ADR 0025's and ADR 0040's functions on an x87 side.

## Context
ADR 0025 makes every floating-point, `decimal` and user-defined operator a pure function both sides share, with no
meaning. ADR 0026 then makes any divergence that depends on one Unknown(abstraction): 246 results on the three
large runs. Old `a * 2.0` against new `a + a` is Unknown, and so is a real change hidden behind an
`IntPtr == IntPtr.Zero` guard. Ticket P1-019 measured the cheapest refinement (ARDiff's idea: start abstract, give
meaning only to what a spurious counterexample implicates) on Git Extensions PR #8522
(`docs/runs/2026-09-30-abstraction-spike.md`): interpreting `IntPtr ==` and `!=` decides 7 of 726 Unknowns, six of
them real divergences. It counted and did not encode floating point, which 38 of the 238 `abstraction` Unknowns
hold and 24 hold alone. Ticket P1-030 builds both.

A second fact forces part of this decision. A `float` or `double` constant is today an element of its sort chosen
by a hash of its text, so the IR does not hold the number `2.0`, and nothing could interpret `a * 2.0`.

## Decision
1. **On demand.** When rung 1 ends Unknown(abstraction) and every abstraction the candidate depends on is an
   interpretable pure function (decision 2), rung 1's product is encoded again with exactly those function names
   interpreted, and rung 1's queries are asked again. Every other function, call and fragment stays as it was.
   - No divergence, and no input past the bound, is **Equivalent**.
   - A model is replayed in `IrInterpreter`, which computes the interpreted functions itself. A divergence in an
     untainted observable is **Divergent**.
   - A model that depends on a further abstraction is refined again when that one is interpretable too, at most
     three rounds in all.
   - No divergence, and an input that reaches an opaque node, is **Unknown(opaque)**: the first query of ADR 0014
     is now unsatisfiable under the real meaning, so the residual claim of ADR 0029 holds.
   - Anything else keeps the Unknown(abstraction) it had, with its first candidate: a query the solver gives up
     on, a further abstraction that is not interpretable, a fourth round, a loop the bound does not cover. A
     refined query that times out is never Unknown(timeout).

   Each refined query has the pair's timeout and ten times the pair's resource limit. The limit was set on
   bit-vector queries (P2-050), and the simplest identity here, `a * 2.0 = a + a` on `double`, needs 3.2 million
   against the default 2 million; `a * 0.5 = a / 2.0` needs 6.8 million. Refined queries go to Z3 alone (ADR 0050's
   second solver is not asked). A pair whose candidate depends on anything that is not interpretable is not
   touched.
2. **Interpretable functions.** These, and nothing else:

   | Function | Meaning |
   |---|---|
   | `op:` `==` and `!=` of `System.IntPtr` and of `System.UIntPtr` | equality of the sort's elements and its negation; never throws |
   | `f32.<op>`, `f64.<op>` for `add sub mul div neg` | IEEE 754 binary32 and binary64, round to nearest, ties to even |
   | `f32.<op>`, `f64.<op>` for `eq ne lt le gt ge` | IEEE comparison: false with a NaN operand, except `ne`, which is the negation of `eq`; `+0` equals `-0` |
   | `conv.f32.f64`, `conv.f64.f32` | exact widening; narrowing rounded to nearest even |
   | `conv.<int>.<float>` from `i8 u8 i16 u16 char i32 u32` | the integer's value, rounded to nearest even |
   | unchecked `conv.<float>.<int>` | truncation toward zero, **only on an argument whose truncated value the target type holds**; on any other argument (out of range, infinite, NaN) the function stays what ADR 0025 made it, shared and tainted |

3. **What makes the floating-point meaning exact.** In a refined query `System.Single` and `System.Double` are
   Z3's `(_ FloatingPoint 8 24)` and `(_ FloatingPoint 11 53)`, inputs, call arguments, call results and map
   elements included, and a shared input is one term. Each operation above is correctly rounded in its own format
   on every runtime this tool compares, except x87 (decision 4): the CLR computes `float` in binary32 and `double`
   in binary64 on SSE2 and on ARM64, and does not fuse a multiply and an add. A refined query's solver turns
   floating point into bit-vectors and those into propositional logic before `smt` (`fpa2bv`, `simplify`,
   `bit-blast`): left to `smt`'s own floating-point theory the same queries cost ten times as much (4.3 s against
   0.33 s for the doubling above).
   - There is one NaN. A NaN's payload and sign are not observables: two values are equal when they are the same
     number, with `+0` and `-0` different and NaN equal to NaN, which is what `Equals` says and what the bits say up
     to a NaN's payload. This is the equality the encoder already uses for every observable (`=` on the sort), so
     a returned `-0.0` against `+0.0` is a divergence and `x == y` on them is `true`.
   - `IrInterpreter` computes the same functions with .NET's own `float` and `double` arithmetic, and a property
     test holds it to Z3 on every interpreted function, with NaN, both zeros, both infinities and subnormals
     among its values. The differential gate (VERIFICATION-MODEL section 7) generates `float` and `double`
     parameters and these operators, and holds its three rules with refinement on.
4. **Where it is refused.**
   - A function that is runtime-sensitive for the pair (ADR 0040) is never interpreted: it has two meanings.
   - A side whose floating point may run on x87 names its floating-point functions `x87.f32.add`,
     `x87.conv.i32.f64` and so on. No `x87.` function is interpretable. Two x87 sides still share them, as ADR 0025
     decides, and a side that alone is x87 still gets side-specific ones. Before this ADR two x87 sides shared
     `f64.add` itself, which an IEEE reading would have got wrong.
   - `%` on floating point (`fp.rem` is IEEE remainder, C#'s `%` truncates), every `dec.*`, a checked
     floating-point to integer conversion, a conversion from a 64-bit integer to floating point (some runtimes
     round it twice), every other `op:`, every `delegate:` and every `get:`.
   - Floating-point to integer conversion outside the target's range: .NET 9 saturates, earlier runtimes give a
     platform's value, and the backend does not know which side of .NET 9 a pair that does not cross it is on.
5. **Constants.** A value of `System.Single` or `System.Double` in the IR is the sort element whose id is the
   value's IEEE bits (binary32 zero-extended), with every NaN the one quiet NaN. A sort element's id is 64 bits
   wide for this. In an unrefined query such a constant is still a designated element, distinct from every other
   constant of its sort, as before; equal constants are still the same element on both sides. In a refined query
   it is its number. A report writes a floating-point value of a model as that number (`f64 0.1`), not as its
   bits.
6. **How ADR 0025's shared-function rule reads.** An interpreted function is still one function of its arguments
   that both sides share: its real one. Sharing proved "equal arguments, equal results"; the meaning adds what the
   result is. Nothing an unrefined query proved is lost, because the real function is one of the functions the
   uninterpreted one ranged over.
7. **How ADR 0026's taint rule reads.** A result of an interpreted function is not an abstraction. The replay
   computes it, so it is tainted only if an argument is. A divergence that depends on it is therefore real, and
   EQ002 stays exact. A floating-point to integer conversion of an argument outside decision 2's range is
   answered from the model and tainted, as any `IrPure` is. Every function that is not interpreted taints as
   before.
8. **How a result says so.** A result decided after refinement has `proofMethod` suffixed `+refined`
   (`bounded+refined`), a Divergent and an Unknown(opaque) included, and carries `properties.refined`, the function
   names interpreted, sorted. Its `ladderTrace` holds rung 1's first step and one step per refined round.

## Why
- The abstraction is right for most pairs and wrong for few. 12,727 of 13,742 results on PR #8522 are proved with
  every operator uninterpreted, at no floating-point cost. Refining only what a candidate names spends
  floating-point solving on the pairs that cannot be decided without it.
- A hidden real divergence is worth more than its count. Six of P1-019's seven refined results were real changes
  that a shared `IntPtr ==` guard kept the tool from reporting.
- Exactness is checked, not argued. The replay computes the interpreted functions with the CLR's arithmetic, so
  a model Z3 gives under a wrong encoding fails to replay as it says and fails loudly (ADR 0014); the property
  test and the differential gate cover the unsatisfiable direction, where a wrong encoding would be a false
  Equivalent.
- Partial meaning for the conversion keeps what is certain. In range, every runtime truncates. Out of range they
  differ, and there the function stays abstract instead of taking one runtime's answer.
- The IR does not change shape. `IrPure` already names the function; only what the encoder and the interpreter do
  with a name changes, and only in a refined query.

## Rejected
- **IEEE sorts for every pair from the start.** ADR 0025 rejected it for cost, and that holds: bit-blasting every
  unchanged `double` computation to prove what sharing proves for free.
- **A floating-point IR type and `IrBinary` operators on it.** A second arithmetic in the validator, the text
  format, the unroller, the Horn-clause encoder and three generators, for values that are abstract in all but a
  few hundred results.
- **A new `IrValue` for a floating-point constant.** Every consumer of values would learn a kind that differs
  from a sort element only in how its id was chosen.
- **Refining when the candidate also depends on something that is not interpretable.** It could prove some pairs,
  but can never report a Divergent (the rest still taints), and no run has counted them. Left for a measurement.
- **Taking .NET 9's saturating conversion as the meaning out of range.** Wrong for every pair before .NET 9.
- **Asking the second solver a refined query.** Its script printer and logic selection know no floating point,
  and a refined query that Z3 gives up on already keeps a sound result.
- **The pair's own resource limit for a refined query.** Measured: at the default limit no floating-point
  identity is proved, not even doubling, so refinement would only ever find divergences.
- **Marking two x87 sides runtime-sensitive.** It would stop them from sharing, and an unchanged computation on
  two x87 sides would no longer be provable.

## Consequences
- An Unknown(abstraction) whose candidate depends only on interpretable functions becomes Equivalent, Divergent or
  Unknown(opaque), or stays as it was. No result that was Equivalent or Divergent changes: refinement runs only
  on an Unknown.
- `IrSortValue.Id` is 64 bits. IR dumps print a floating-point constant's bits where they printed a hash, so
  snapshots that hold one change once. `--execute` passes a model's floating-point value as the number it is.
- `IrInterpreter` takes the set of interpreted function names. The catalogue of what is interpretable and the
  concrete arithmetic live in `Equiv.Core`; the Z3 terms live in `Equiv.Verify.Z3`.
- An x87 side's abstractions are named `x87.…` in `properties.abstractions`.
- VERIFICATION-MODEL sections 2, 3, 5 and 6 and ROADMAP's post-MVP "Floating point as IEEE sorts" line change in
  the ticket's PR. What remains post-MVP: `%`, `decimal`, strings, a user-defined operator's body, and
  floating point in rungs 2 to 5.
- Ticket: P1-030. The reverse direction, abstracting more to beat a timeout, is P1-031.
