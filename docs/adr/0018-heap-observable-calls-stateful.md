# ADR 0018: The final heap is observable, and a call is a function of its position and the heap

Status: accepted (2026-09-21). Supersedes ADR 0015 in part: P1-005 and P1-006 stop being
post-MVP limits and land before M3-003.

## Context
The pre-M3 architecture review traced three ways the spec as written proves a false Equivalent,
all silently (no `IrOpaque`, no Unknown):
1. VERIFICATION-MODEL section 2: "The final heap is not an observable". So
   `void Set(int v) { this.x = v; }` is Equivalent to `void Set(int v) { }`, and so is every setter
   and every void method that mutates state. ADR 0015 did not name this gap.
2. M3-001 encodes a call as `f_callee(args)`, and `ICallOracle` must be "deterministic in (callee,
   arguments)". That makes two calls with the same arguments on one side return the same value, so
   `a = r.Read(); b = r.Read(); return a == b;` is Equivalent to `r.Read(); r.Read(); return true;`.
   `DateTime.Now`, `reader.Read()` as a loop guard, and `rng.Next()` all break the same way.
3. A call does not see the heap. `x = 1; Save(this); x = 0;` and `x = 2; Save(this); x = 0;` have
   equal traces, equal results and an equal final heap. ADR 0015's P1-005 closes the output side
   (the call writes the heap) but not the input side (the call reads it).

## Decision
**Heap as output.** The synthesised `field.*` and `array.*` map parameters are `Ref`, not `In`.
They then carry `outs` on every exit, so the final heap is an observable exactly as a C# `ref`
parameter is. `null.*` and `length.*` stay `In`, because no instruction changes nullness or length.
When one side of a pair has a `Ref` parameter the other side lacks, that is a heap slice the other
side never touches, and the encoder compares the first side's final value against the shared input.

**Calls are stateful.** A call's result, `threw` flag and heap effect are uninterpreted functions
of (callee, arguments, the heap at the call, the call's position in its own side's trace). Each
side shares these functions with the other side, as the mutual-summary assumption already
requires. The heap at the call is part of the call's trace event, so a pair whose heaps differ at a
call has differing traces. For each map name in the union of both sides, "the heap at the call" is
the side's current version of that map, or the version the encoder threads through that side's
calls when the side never names it. The position is the number of calls the side has executed
before this one. `ICallOracle.Answer` takes that position, and replay oracles key by it.

Staging: M3-001 lands position keying and the one-sided `Ref` comparison. M3-007 makes the
frontend emit `Ref` heap maps. P1-005 lands the heap as input and output of a call. All three land
before M3-003, and so does P1-006, so no build that reports verdicts on samples carries a known
silent false Equivalent.

## Why
- Keying by position is sound. If two executions diverge, take the first trace position where they
  differ. Every call before it has the same history on both sides, so a model that gives
  same-position, same-argument calls the same answer can reproduce the real results up to that
  point, and the traces still differ there. A pair with equal traces has equal histories at every
  call, so no precision is lost where it matters.
- Keying by position is cheaper than keying by the whole trace prefix (one integer term instead of
  a `Seq` argument per call) and is equally sound, by the argument above.
- Treating the heap as `Ref` reuses the observable the encoder already compares. It needs no new
  IR construct.
- The review of M2-004 showed that a lowering gap sitting under 100% coverage is invisible to the
  IR-level soundness harness. Leaving the call gaps for a post-MVP milestone would ship the first
  real-world run (M3-005) with Equivalent verdicts nobody should trust.

## Rejected
- **Final heap stays unobserved and void mutators are documented as a limit.** Mutators are most
  of the code in a typical C# business layer, so this limit would cover the main use case.
- **Key calls by the full trace prefix.** Equally sound, but it adds a `Seq<Event>` argument to
  every function application.
- **Make every call on a procedure that touches the heap `IrOpaque`.** Sound, but it makes nearly
  every real method Unknown (ADR 0015's reason for rejecting it still holds).
- **Pass the whole heap as ordinary `Args`.** The two sides name different maps, so the argument
  lists and function arities would no longer line up across the pair.

## Consequences
- Precision cost, accepted under "false alarms are cheaper than false proofs":
  - A side that writes a field on an object it then discards differs in its final heap.
  - A side that writes a field before a call where the other writes it after differs in the
    call's heap argument, even if the callee never reads that field.
- VERIFICATION-MODEL: section 1 (observables), section 2 (synthesised inputs, `IrCall` row,
  the two-gaps paragraph), section 5 (call encoding), section 7 (soundness mutations) are updated
  in the same PR as this ADR.
- M3-001 gains criteria for position keying, the one-sided `Ref` comparison, the `ICallOracle`
  change and three fixtures. M3-002 threads the position counter through its loop state.
- New ticket M3-007 (heap maps become `Ref`). P1-005 is rewritten so that the heap is both an
  input and an output of a call. P1-005 and P1-006 move into M3's ordering, ahead of M3-003.
- Once P1-005, P1-006 and M3-007 have landed, ADR 0015's two gaps are closed.
