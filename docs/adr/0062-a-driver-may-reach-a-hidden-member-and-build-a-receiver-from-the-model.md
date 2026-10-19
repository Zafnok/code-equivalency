# ADR 0062: A replay or `probe` driver may reach a member the friend assembly cannot name, and build a receiver from the model's fields; each input says how it was built

Status: accepted (2026-10-19), as revised by ticket P2-245 (decision 3's delegates and tokens). Supersedes in part ADR 0035 ("There is no reflection anywhere: the driver is generated
source") for generated driver source only, and VERIFICATION-MODEL section 6's replay paragraph ("an instance method on
`new T()`", "replay builds no object graphs"). Ticket P2-245 implements it.

## Context
P2-237 (`docs/runs/2026-10-17-precision-execute-verdict.md`) replayed every Divergent of the three migration pairs: all
37 EQ002 replays were `not-constructible`, and the 16 confirmations came from `probe` on inputs a hand trace chose. Over
the 277 `not-constructible` replays, 59 name a member that is not public, 49 a model that constrains a field, a cast or
an array, 27 a receiver with no public parameterless constructor, 5 an argument the driver cannot build. Of P2-212's 71
results still undetermined, 41 no driver can call (private, no public parameterless constructor, abstract) and 10 need an
argument no driver builds (`CancellationToken`, a delegate, an array, a struct, an object). Generated source calls a
`private` member only through reflection, and ADR 0035 says the driver uses none. The legacy sides run on .NET Framework
4.8 or Mono and the modern sides on .NET 5, where `UnsafeAccessor` (.NET 8) does not exist.

## Decision
1. **Generated driver source may use reflection for two things, and `src/` still uses none.** (a) To reach a member, or
   a member of a type, that the driver's friend assembly cannot name (`private`, `protected`, `private protected`): it is
   bound once by its exact signature and then called as a direct call would be, its exception reaching the outcome
   unwrapped and no argument converted. A member not bound by its exact signature is `not-constructible`. When either
   side's member needs it, both sides' drivers reach their member this way, so the two calls have one shape. (b) To create
   an instance of a type without running a constructor and write its instance fields (decision 2). Nothing else: no
   `dynamic`, no Reflection.Emit, no assembly scanning, and the generator in `src/` stays plain Roslyn.
2. **A replay's receiver, and an object argument, are the model's state.** When the model constrains no field the two
   bodies read, the receiver is `new T()` where `T` has a public parameterless constructor, as today, and otherwise an
   instance of `T` created without a constructor with every field at its default: the model leaves the state free, so
   both are its state. When the model constrains fields, the instance is created without a constructor and each field
   the model gives is written with the model's value; a reference-typed value is built by the same rule, one instance
   per model element (shared where the model shares it), a string, enum or primitive as replay builds it today, and an
   array from the model's length and elements. A cast or type-test map that names a concrete type builds that type: an
   element and the elements its upcasts, and its casts whose type test passes, yield are one object, of the one type each
   of them is. Casts that make two references of one sort one object, a type test of a type the side does not resolve,
   and a `new.*` map that gives a body's fresh array an input's reference are states no real run has, so not
   constructible. Only
   a class or struct declared in the solution's own source is ever built without a constructor; a framework or
   third-party type is built only by a public constructor (or `null`), because an instance whose invariants its own
   constructor never set would differ between two runtimes' internals, not between the two sides of the pair. A replay
   never runs a constructor to build a state the model constrains: its state would not be the model's, and a
   `not-reproduced` on it would be a false alarm.
3. **The argument kinds the model determines are built.** A `CancellationToken` is `default`, or a cancelled token when
   a run of the model ends in `OperationCanceledException`, the one way the model's run observes cancellation that the
   counterexample shows. An array is built from the model's array map, its elements at their defaults where the bodies
   read none. A delegate is a generated function of the parameter's type. Where neither of the model's runs calls through
   it, any answer is the model's, and it throws a fixed exception if called, which no run of the model does. Where a run
   calls it, its answers are the model's interpretation of that open call (its result, or its throw), the same table on
   both sides; the counterexample a replay is given carries no call answers, and recording them is a change to the
   solver's model decoder, so until ticket P2-249 such a delegate is not constructible with that reason. A struct whose fields are arrays, strings or objects is built
   field by field by decision 2's rules, extending ticket P2-242's flat structs. A parameter whose type is a window or a
   desktop-session type stays not constructible (ADR 0035's 2026-09-29 clarification), as does an `extern` method.
4. **`probe` takes the same inputs by name.** A receiver is either a constructor and its JSON arguments, or a type and
   its fields as a JSON object (solution types only, by decision 2); an argument may be an array, a cancelled or live
   token, a delegate as a constant answer, or an object by a public constructor or by fields.
5. **Each input says how it was built.** A replay result's `properties.replayInputs` and a `probe` result list, for the
   receiver and each argument, one of `value`, `constructor <signature>`, `uninitialized <fields>` or `delegate`. A
   precision audit counts a difference whose receiver or argument is `uninitialized` with model fields as confirming a
   Divergent only when a hand trace shows that some constructor and calls of the solution reach that state; otherwise
   the result stays undetermined with that reason.

Replay and `probe` still never change a verdict, a rule id, a fingerprint or an exit code (ADR 0035): execution confirms
and never proves. `not-reproduced` keeps its meaning, a soundness or modelling finding, because every input it ran on is
the model's.

## Why
- The model is a claim about arbitrary states: a field the bodies read is a free input, constrained only by what the
  solver chose. Writing exactly those values is the only receiver on which both `reproduced` and `not-reproduced` keep
  ADR 0035's meaning; a constructor's state is the model's only when the model constrains nothing.
- Reflection is the one mechanism every driver runtime has: .NET Framework 4.8, Mono and .NET 5 and later. Binding once
  by exact signature and calling directly keeps a reflection artefact (a wrapped exception, a converted argument) out of
  the outcome, and one call shape on both sides keeps it out of the comparison.
- The rule against reflection protects equiv's own code: its coverage, its analysers, its trimming. A driver is text that
  equiv writes and a child process runs; equiv itself gains no reflection.
- Reachability is a separate question from the model. Recording `uninitialized` lets an audit ask it, instead of either
  counting an unreachable state as a confirmed false positive or refusing to build the state at all.

## Rejected
- **`UnsafeAccessor`:** .NET 8 and later only, and a private type in a signature needs .NET 10's `UnsafeAccessorType`.
  No legacy side and none of the three migrations' modern sides (.NET 5) could use it.
- **Raising the member's accessibility in the emitted compilation** (`private` to `internal`, beside the friend
  attribute P2-052 adds): it changes the program under test, since overload resolution elsewhere can bind to the newly
  visible member and reflection over the program sees it, so a difference could come from the rewrite.
- **`IgnoresAccessChecksToAttribute` with Roslyn's internal accessibility-ignoring binder flag:** the flag is internal to
  Roslyn, reachable from `src/` only by reflection, and the attribute is a CoreCLR loader behaviour, not a contract .NET
  Framework 4.8 keeps.
- **A trace-chosen or "simplest" constructor for a constrained receiver in replay:** its state is not the model's, so
  `not-reproduced` would stop meaning the model is wrong. `probe` may take one because an agent names it.
- **Building framework and third-party types without a constructor:** their invariants live in runtime internals that
  differ between .NET Framework and .NET, so a difference would be a harness artefact reported as `reproduced`.
- **Leaving the obstacles in place:** 51 of the 71 undetermined results stay undetermined for want of a call, and replay
  confirms nothing on EQ002.

## Consequences
- More of the user's code runs under `--execute`: private members, and instances that skip their constructor. It stays
  opt-in, printed on stderr, and never on by default (ADR 0035).
- `replayReason` loses `not public (...)` and `has no public parameterless constructor` except for what decision 2 and 3
  leave out (abstract receivers with no solution type the model names, framework or third-party receivers with no public
  constructor, window types, `extern`); `the model constrains ...` remains only for maps the drivers still cannot build.
- `properties.replayInputs` is a new property; the implementing commit carries `Release: minor`.
- VERIFICATION-MODEL section 6's replay paragraph and ADR 0035's Status line change in the PR that implements and
  accepts this ADR (ticket P2-245). CLAUDE.md's "No reflection" rule gains a pointer here for generated driver source.
