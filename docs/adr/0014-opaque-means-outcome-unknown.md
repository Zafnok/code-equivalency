# ADR 0014: Reaching an `IrOpaque` makes that input's outcome unknown

Status: accepted (2026-09-19)

## Context
VERIFICATION-MODEL.md section 2 says `IrOpaque` "poisons every dependent value", and M3-001
turns that into a def-use rule: `Unknown(opaque)` only when an opaque value flows into an
observable. That is sound only if every opaque node is a side-effect-free value. M2-003
(PR #27) lowers opaque *effects* too. On `main` today, `x += 1; return x;` lowers to
`%$0 = opaque "CompoundAssignment"; ret %x`. `void M(int a) { if (a < 0) throw …; }` lowers
the throw to `opaque "Throw"; ret`. A void body with a loop is `opaque "loop"; ret`.
`s.ToUpper();` and `F(ref x)` drop the call and leave `x` unchanged. Under the def-use rule,
M3-001 would report every one of these Equivalent to the same method without the construct.

## Decision
An `IrOpaque` stands for "from here on, this execution is not modelled". An input on which
either side executes an `IrOpaque` has an unknown outcome. That matches `IrInterpreter`
today, which stops with `IrOpaqueReached`. The backend decides a pair in two queries.
(1) Some observable differs AND neither side reaches an opaque block: satisfiable gives
Divergent, and the counterexample replays in the interpreter without reaching an opaque.
(2) Otherwise, if either side can reach an opaque block (`OR reach.B` over blocks that
contain one), the verdict is `Unknown(opaque, reasons)`. If neither can, it is Equivalent.
The static def-use short circuit is removed.

## Why
- It holds whatever the frontend leaves out: a dropped write, a dropped call, a throw
  turned into `ret`, a missing `ref`-argument update. A frontend bug in an unsupported
  construct can then cost precision, but it cannot produce a false Equivalent.
- It gives one meaning to both consumers of the IR: the interpreter and the encoder.
  Today they disagree.
- It is path-sensitive, not "any opaque in the body". A divergence on a path with no
  opaque is still reported, and an opaque behind a branch that no input takes costs
  nothing.
- Fixing the lowering alone is not enough. M2-004 still introduces more opaques
  (`rethrow`, `catch-filter`, `foreach-enumerator`, `switch-pattern`), and every future
  construct would need the same discipline, with no test that could notice a lapse.

## Rejected
- Keep the def-use rule and make the lowering route every effect into a value (write the
  opaque into assignment targets, havoc `ref` arguments, never end an opaque path with a
  plain `ret`): correct only while every future lowering rule remembers to do it.
- Any opaque anywhere in the body gives Unknown: needlessly loses the counterexamples on
  paths with no opaque.
- A `pure` flag on `IrOpaque` (string literals, non-integral constants) so the def-use
  rule can still apply to side-effect-free values: changes a Core record for a precision
  gain nobody has measured yet. Revisit if M3-005 shows many `Unknown(opaque)` results
  caused only by pure values.

## Consequences
- Precision loss: a string literal or other non-integral constant is opaque until M2-004
  or later, so any path through one becomes Unknown instead of Equivalent. That is
  accepted, because "false alarms are cheaper than false proofs".
- VERIFICATION-MODEL.md, on acceptance:
  - Section 1: "an `IrOpaque` node flows into an output" becomes "some input reaches an
    `IrOpaque` node on either side".
  - Section 2, `IrOpaque` row: "poisons every dependent value" becomes "execution past
    this point is not modelled; an input that reaches it has an unknown outcome
    (ADR 0014)".
- M3-001, on acceptance:
  - The Design bullet "`IrOpaque` reaching an observable … short circuits" is replaced by
    the two-query rule above.
  - A new acceptance criterion: fixtures `opaque-void-effect` (a void pair that differs
    only by an opaque statement gives Unknown(opaque)) and `opaque-other-path` (a
    divergence on a path with no opaque, next to an opaque branch, gives Divergent, and
    the replay does not reach the opaque) produce the verdict in their first comment line.
  - The soundness property's `Mutate(P)` may insert an `IrOpaque`, and the result must
    never be Equivalent.
- M2-004, on acceptance: a new acceptance criterion states that compound assignment and
  `++`/`--` on integral locals and parameters lower as read, operate, write (honouring
  `checked`), with a snapshot test and the oracle generator extended. AC8 already forces
  this implicitly (`loop-bound-change` uses `i++` and `sum += i`), and the criterion makes
  it explicit. `ref`-argument calls stay opaque; with this rule that is only imprecise,
  not unsound.
- No code changes. M2-003's lowering stays as merged.
