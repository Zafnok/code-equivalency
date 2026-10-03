# ADR 0043: A call to a static one-call forwarder is a call to its target

Status: accepted (2026-10-02). Narrows ADR 0019's rejection of inlining for one shape of callee, and
VERIFICATION-MODEL section 3's rule that two different callee identities are two different functions.

## Context
P2-047's audit found 11 false EQ002 among 31 audited, the largest cause (ticket P2-068). The legacy
side calls a helper of its own, `Strings.IsNullOrEmpty(s)`, whose whole body is
`string.IsNullOrEmpty(s)`. The modern side calls `string.IsNullOrEmpty(s)` directly. Section 3 gives
the two identities two unrelated uninterpreted functions and two different trace events (ADR 0018),
so every input is a counterexample. Unlike ADR 0042's rebound calls, nothing here is unknown: the
helper's body is in the run, and it says the two calls are one call.

## Decision
A **forwarder** is a method declared in the source of the solution being lowered that is all of:
- an ordinary static method that is not `virtual`, not generic and not in a generic type, with no
  `ref`, `out` or `in` parameter, that does not return by reference, and whose declaring type has no
  static constructor, written or implied by a static field or property initializer;
- one whose body, block or arrow, is exactly one statement: `return G(p1, ..., pn);`, or `G(p1, ..., pn);`
  when it returns nothing;
- where `G` is a static method, each argument is written explicitly and is the forwarder's own
  parameter at the same position with nothing applied to it (no conversion node), every parameter
  is passed, `G` has no other parameter, and the parameter types and the return type of the two
  methods are the same.

`G` is the forwarder's **target**. When the target is itself a forwarder the chain is followed to its
end, and a chain that comes back to a method already on it is not followed at all. A call to a
forwarder is lowered as the same call to the chain's last target: that callee identity, the same
arguments in the same order. Everything that reads a callee then reads the target: the rename map,
the runtime-changes table, the API-equivalence catalogue (ADR 0020), whether the call is closed (ADR
0041), rebound call sites (ADR 0042, whose key keeps the site's own text and the member name written
there), and the bound fingerprint (ADR 0024), which spells an invocation's callee as the IR names
it. Both lowerings apply the rule from the callee's symbol, so they still produce the same IR (ADR
0039). It applies on both sides and to every call, whether or not the forwarder is a matched pair.
Every result of a pair where a forwarder was resolved in either body carries
`properties.forwardersResolved`: one `{ forwarder, target }` per forwarder, sorted.

## Why
- It is the forwarder's meaning, not an assumption. With no type initializer to run, calling it does
  one thing: it calls the target with the same values and gives back what the target gave, or
  throws what the target threw. The trace event, result, `threw` flag and heap effect of the target
  call are all of the forwarder call's observable behaviour. What differs is a stack frame, which no
  observable in section 1 holds.
- ADR 0019 rejected inlining because it explodes on recursion and deep call chains and discards
  modularity. A forwarder has no branch, no loop and exactly one call, so replacing the call adds no
  instruction and no path, and a cycle is refused. The caller's verdict stays modular in the target.
- Reading the body is more precise than ADR 0019's assumption, and never less sound. If a forwarder
  changed its target between the sides, its callers used to be Equivalent "assuming the forwarder",
  and are now compared on the two targets.
- The fingerprint must follow, or congruence would decide a pair from callee names the lowering no
  longer uses, and an Equivalent by congruence would not name the forwarder it assumed.
- The conditions are the ones a symbol and a one-statement body can show without lowering the
  forwarder. Each one that is dropped needs something the caller's lowering would have to model.

## Rejected
- **A catalogue entry per helper (ADR 0020).** The catalogue is shipped and cited. A helper is the
  user's own code and differs per solution.
- **Leaving it to the config's call-identity map.** The user would have to find and list every
  wrapper by hand. The tool can read the body and they cannot be wrong about it.
- **One shared function for the forwarder and its target, listed as an assumption.** Nothing is
  assumed, and the trace events would still need the two identities unified.
- **Rebound-style Unknown (ADR 0042).** The bodies are known, so Unknown would throw away a proof.
- **Inlining any single-expression or small callee.** That is ADR 0019's rejected option, and needs
  the callee's body lowered into the caller: its branches, its own calls, its heap reads.
- **Instance forwarders, and instance targets called on a parameter (`s.Trim()`).** Sound, but the
  call then has a receiver whose null check, virtual dispatch and catalogue adapter each need wiring
  in both lowerings. None of the audited cases needs it. A later ticket can extend the rule.
- **Arguments that are constants, reordered, or converted.** A reordering changes evaluation order
  only for by-value parameters already evaluated, so it would be sound, but each of these is a
  rewrite of the argument list rather than the same call. Not needed by the audited cases.
- **Generic forwarders.** The target's type arguments would have to be substituted per call site.

## Consequences
- A pair whose only difference is a forwarder replaced by its target, or the reverse, moves from
  EQ002 to EQ001. When nothing else differs the bound fingerprints are equal, so congruence decides.
- A caller of a forwarder no longer lists it in `assumedCallees`, and lists the target when that is
  a matched pair. A forwarder whose own body changed is still its own result.
- A call to a forwarder to a runtime-changed member is now EQ006 in the caller, where it was hidden
  behind the forwarder's identity. That is the real behaviour.
- A forwarder that changes an argument, adds a statement, or sits in a type with a static
  constructor is not one, and its callers are compared as before.
- A forwarder declared in another project is resolved when that project is loaded from source in the
  same run. One that is only a compiled reference has no body to read and stays an ordinary callee.
- The census counts the target, not the forwarder, in `externalCallees` and the runtime-change counts.
- SARIF gains `properties.forwardersResolved`. Verdicts change on existing input, so the change that
  implements this carries `Release: minor`.
- VERIFICATION-MODEL sections 3 and 6 change in the PR that accepts this ADR. Ticket P2-068
  implements it, with `samples/forwarder-to-bcl`.
