# ADR 0021: Source-language parameters are shared by position, synthesised inputs by name

Status: accepted (2026-09-21), in the review of M3-001 (PR #78).

## Context
VERIFICATION-MODEL section 2 and the M3-001 Design shared a pair's inputs by parameter name. The
C# frontend names IR parameters after the C# parameters, and the matched identity records only their
types. A caller binds arguments by position, so the name rule proved a false Equivalent:
`int Sub(int a, int b) => a - b` against `int Sub(int b, int a) => a - b` was Equivalent, though
`Sub(5, 3)` is 2 on one side and -2 on the other. It also gave a false Divergent for a plain
parameter rename, and threw `ArgumentException` (crashing the CLI) when a synthesised input such as
`field.C.x` kept its name but changed type. The generated soundness harness could not see any of
this: no mutation touched the signature.

## Decision
The product encoding pairs the two sides' parameters into shared inputs. Source-language parameters
pair by position. Synthesised inputs (VERIFICATION-MODEL section 2: `this`, and every name spelled
with a dot, which no source-language parameter can be) pair by name. Two parameters pair only when
their types are equal; any other parameter is an input of its own side alone. A by-ref shared input's
final value is compared across the sides, a side without the parameter standing for its unchanged input.

## Why
- Position is how a caller binds arguments, and a matched pair has the same parameter types in the
  same order (the identity says so), so positional pairing shares exactly what callers share.
- Synthesised inputs are the heap, which both sides see under the same field name; that is ADR 0018's
  one-sided comparison, unchanged.
- Refusing to share differently typed parameters can only add behaviours, so it costs precision (a
  false Divergent) and never soundness, and it turns the type-change crash into a verdict.
- Tried: sharing the first N parameters, with N parsed from the identity's parameter list. Hand-written
  IR fixtures do not keep identity and signature in step, and parsing generic type names is fragile.

## Rejected
- An explicit `Synthesised` flag on `IrParameter`: correct, but a Core contract and IR text change
  for a distinction the naming rule already makes.
- Keeping name sharing and asking the matcher to include parameter names: a rename would stop matching
  and become Added plus Removed.

## Consequences
- `ProductEncoder.Pair` is the one place the rule lives; the replay binds inputs through it too.
- VERIFICATION-MODEL section 2 states the pairing rule, and the naming rule becomes a frontend
  obligation: a synthesised input's name contains a dot or is `this`, and a source-language parameter's
  never does. A future frontend (Java) keeps it.
- `IrGen.Mutation` swaps two parameters in the signature, and `IrGen.AcyclicRenamedPair` feeds a
  property that a renamed procedure stays Equivalent.
- A C# parameter named `@this` would collide with the receiver input; that is a frontend bug to fix
  on its own, not a case this rule covers.
