# ADR 0045: A call to a private helper that exists on one side only is replaced by the helper's body

Status: accepted (2026-10-02). Narrows ADR 0019's rejection of inlining to callees that are matched
pairs, and ADR 0018's rule that every call is a trace event.

## Context
P2-049's sample `cleanup-extract-method` holds the two commonest structural cleanups (ticket P2-097).
Modern `Invoice.Total` moves its discount computation into a new private `Discount`, and modern
`Invoice.Shipping` has the body of legacy's private `Billable` written in place. Both callers are
Divergent (EQ002), and both are false. The counterexample returns the same value on both sides; the
only difference is one trace event, the call to the helper (ADR 0018). The helper is Added or
Removed, so it is no pair: nothing verifies it, ADR 0019 has no callee pair to assume equivalent,
and the caller is the only place its body can be checked. ADR 0043 covers only a helper whose whole
body is one static call.

## Decision
A **one-sided helper** is a method declared in the source of one side that is all of:
- `private`, an ordinary method (not an accessor, constructor, operator, local function or lambda),
  static or instance, with a body, and not `async`, not an iterator, not `extern`;
- not generic and not in a generic type, with no `ref`, `out` or `in` parameter, and not returning
  by reference;
- unmatched: after the rename map and identity normalisation its identity is Added or Removed. A
  matched or Ambiguous identity never qualifies;
- lowered without a lowering failure, and not on a call cycle: it does not call itself, directly or
  through other one-sided helpers;
- within the bound: its **expanded body**, its lowered body with every call this rule resolves
  inside it replaced in turn, has at most 256 IR instructions.

A call to a one-sided helper from a method declared in the helper's own type is **resolved**: the
call is replaced by a copy of the helper's expanded body. The source parameters are bound to the
call's arguments and `this` to its receiver, both already evaluated, the receiver already
null-checked. The helper's synthesised inputs become the caller's own, by name (ADR 0021), and the
caller gains any it lacks; a heap map enters the copy at the version the caller holds at the call
and leaves it as the caller's next version. The caller's body is then the body it would have with
the helper's code written at the call: its other calls list the maps the helper touches too.
- **Trace.** The helper's call has no trace event, result function, `threw` function or heap
  function. The calls its body makes are the caller's calls, in the caller's trace, counted in the
  caller's positions.
- **Return.** Each returning exit of the copy continues after the call with the returned value.
- **Throw.** Each throwing exit keeps its exception type. One of type `System.Exception`, the type a
  call's `threw` edge carries, takes that edge. One of any other type is the caller's own throw of
  that type, and needs the call to sit outside every `try`, `catch`, `finally`, `using`, `lock` and
  `foreach` region of the caller; a call inside such a region to a helper with such an exit is not
  resolved.

A call that is not resolved is an ordinary call, as before. The frontend resolves, after matching
and on the lowered IR, so both lowerings take the rule, each with the helper's body from the same
lowering (ADR 0039). `ProcedurePair` carries what was resolved, as it carries `ReboundCalls`. Every
result of a pair where a call was resolved in either body carries `properties.calleesInlined`: one
`{ callee, side }` per helper, `side` being `legacy` or `modern`, helpers resolved inside helpers
included, sorted by callee and then side. It is not part of the fingerprint. The helper keeps its
own Added (EQ004) or Removed (EQ005) result.

## Why
- A call is a trace event because its effects are unknown: the event, with the shared functions
  behind it, stands for whatever the callee does. Here the body is in the run, so its effects are
  stated exactly: its calls, its heap writes, its throws, its result. What is left of the call is a
  stack frame, which no observable in VERIFICATION-MODEL section 1 holds. Nothing is assumed, so the
  Equivalent is a proof and not a named assumption.
- ADR 0019 chose shared functions over inlining because the callee pair gets its own verdict for the
  caller to rest on. A one-sided helper gets none. Modularity has nothing to keep there, and the
  alternative to reading the body is to report a difference nobody can observe.
- ADR 0019's two objections are met by the conditions: a helper on a cycle is never resolved, and
  the bound caps what one call site can add at 256 instructions. One number bounds depth as well,
  since a chain of helpers has to fit inside the first one's expanded body.
- `private` and "from the helper's own type" are what make the call fully known from a symbol: it
  cannot be virtual, an override or an interface dispatch, so the body read is the body run, and
  the declaring type is already being initialised when the caller runs, so the call starts no type
  initializer.
- Exception types must survive. Collapsing a helper's `throw new ArgumentException()` to the
  `System.Exception` of a call's `threw` edge would make two helpers that throw different types
  agree, a false Equivalent. Inside a protected region the caller's handlers were lowered for a
  throw of unknown type, and the IR cannot route a typed one, so that case is left unresolved
  rather than guessed.
- It is more precise than today and never less sound. A helper that adds a real effect (a new call,
  a field write) shows that effect in the caller's trace or heap, with a counterexample that names
  it, where today's counterexample names only the helper.
- Core already inlines a procedure's body at a call for rung 1's self-calls (`IrUnroller`), with
  these bindings, so the rule adds no IR construct and no backend change.

## Rejected
- **Drop the helper's trace event and keep the call as a shared function.** There is no call on the
  other side to share it with, so its result would be the model's choice and the pair Divergent on
  the return value instead.
- **Unknown with a new reason instead of Divergent.** It stops the false alarm and proves nothing.
  The body is known, so Unknown would throw away a proof, as ADR 0043 found for forwarders.
- **Inlining matched callees.** ADR 0019 stands. A callee matched on both sides is a shared function
  with its own verdict, also when only one side's caller calls it.
- **Any accessibility.** An `internal`, `protected` or `public` method can be virtual, can sit in
  another type whose initializer the call runs, and when `public` is itself part of the compared
  surface. Ticket P2-097 leaves these out of scope.
- **A depth bound beside the size bound.** A second knob that the size bound already implies.
- **A configurable bound.** No pair needs it yet, and the sample's helpers are a few lines each.
- **Lowering the helper's syntax at the call site in the frontend.** It could route a typed throw
  through the caller's handlers, but it needs the rule written twice, once per lowering, and the two
  must still produce the same IR (ADR 0039).
- **Pairing a helper with the other side's code by similarity.** That is guessing a match. The
  matcher is by identity (section 4).

## Consequences
- A pair whose only difference is code moved into, or out of, a private helper moves from EQ002 to
  EQ001, proved by the ladder like any pair (`proofMethod` is unchanged). So does a private helper
  that was renamed without a rename-map entry: each side's helper is one-sided and both are
  resolved.
- Congruence (ADR 0024) is untouched. Equal bound fingerprints spell equal callee identities, and an
  identity on both sides is matched, so a congruent pair calls no one-sided helper.
- A matched callee the helper calls joins the caller's `assumedCallees`. A runtime-changed call in
  the helper is the caller's EQ006. An `IrOpaque` in the helper makes the caller Unknown on the
  inputs that reach it (ADR 0014). Each is the real behaviour of the caller.
- Not covered, and compared as before: a helper that is recursive, over the bound, generic, `async`,
  an iterator or takes a by-ref parameter; a helper in another type or not `private`; a typed throw
  inside a protected region; a helper that exists on both sides when only one side's caller calls
  it; a rebound call site (ADR 0042) that moved into a helper, since its key is found per written
  body.
- A body can grow by up to 256 instructions per call site, which costs solver time on callers with
  many resolved calls.
- The census and `--dump-ir` read the caller's body after resolution, so a helper's opaques and
  external callees count in each caller that resolves it.
- SARIF gains `properties.calleesInlined`. Verdicts change on existing input, so the change that
  implements this carries `Release: minor`.
- VERIFICATION-MODEL sections 1 and 6 change in the PR that accepts this ADR. Ticket P2-097
  implements it on `samples/cleanup-extract-method`.

## Clarifications
- 2026-10-03 (P2-068). Where this ADR says "ADR 0043" of forwarders (Context, and Rejected under
  rebound-style Unknown), it means the forwarder ADR, which was proposed as 0043 and merged as ADR
  0047. On `main`, ADR 0043 is the effect-free BCL members. A helper that is both a forwarder and a
  one-sided private helper is resolved by ADR 0047 while its caller is lowered, so no call to it is
  left for this ADR to replace; the caller lists it in `forwardersResolved`, not `calleesInlined`.
