# P2-158 A closed member that never throws has no `threw` flag, so a divergence after it is still Divergent
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-081

## Goal
ADR 0056 taints a closed call in the replay. Each side branches on the call's tainted `threw` flag,
so everything a side does after a closed call is tainted, and a real divergence that shows only
after one is Unknown(Abstraction). For a member that never throws the branch is not needed:
`Nullable<T>.GetValueOrDefault()`, `Nullable<T>.HasValue`, `String.Concat(string, string)`. When
this is done, a call to a closed member on a short reviewed list has no `threw` flag in either
lowering. Its result and event stay tainted (ADR 0056), so a pair that differs after such a call,
in an observable that does not use the call's result, is Divergent again.

## Spec references
ADR 0056 (Consequences, and the summary it rejects as the whole answer), ADR 0041, ADR 0043 (the
precedent for a reviewed list of members), ADR 0026, VERIFICATION-MODEL sections 1, 2 (the `IrCall`
row) and 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome (a clarification on ADR 0056 or ADR 0043, or a new ADR) is merged before
   any code change. It names the members and says why each never throws on a non-null receiver.
2. `## Notes` gives the count of results that are Unknown(Abstraction) naming a closed callee in the
   latest corpus run under `docs/runs/`, or says that no run after P2-081 exists yet, and lists the
   closed callees named most often. The list of members comes from that count where it exists.
3. A call to a listed member has no `threw` flag from IOperation and from IL.
4. A pair whose sides differ after such a call, in an observable that does not use its result, is
   Divergent. A pair whose divergence uses the result is still Unknown(Abstraction).
5. The differential gate's rules 1 to 3 hold on the PR and nightly budgets under both lowerings,
   with rule 2's exemption unchanged (open calls only).

## Tests
- `DifferentialSoundnessTests.ADivergenceAfterAClosedMemberThatNeverThrowsIsDivergent`
- one unit test per listed member, in `Equiv.Frontend.CSharp.Tests`

## Out of scope
A member's result (non-null, or its real value). Open calls. Any member that throws for some
argument (`int.Parse`, `String.Substring`).

## Notes
- Filed 2026-10-11 by P2-081, whose ADR names it.
