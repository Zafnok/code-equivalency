# P2-159 The closed callees an Unknown(Abstraction) names on the corpus are counted, and the ones that never throw join the list
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-081

## Goal
ADR 0056 taints a closed call in the replay. A real divergence that shows only past a closed call
that can throw, or that both sides reach only through a closed call's answer, is now
Unknown(Abstraction) naming the callee. P2-081 measured the cost on the samples (no rule id
changed) but not on the corpus. ADR 0056's decision 3 gives a call to a listed member no `threw`
flag, and its list was chosen from the samples and the differential gate. When this is done the
corpus says which closed callees cost the most, the list holds every one of them that never throws,
and each of the rest has a ticket or a stated reason to stay Unknown.

## Spec references
ADR 0056 (decision 3 and Consequences), ADR 0041, ADR 0043 (the precedent for a reviewed list of
members), ADR 0026, VERIFICATION-MODEL sections 1 and 6, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` gives, from a corpus run made after P2-081 merged, the count of results that are
   Unknown(Abstraction) naming a closed callee, and the count per callee.
2. Every callee among the ten most named is in one of three places: the list (with one line in a
   dated clarification on ADR 0056 saying why it never throws), a filed ticket (a member whose real
   meaning or whose result's range would decide the pair, such as `Nullable<T>`'s members under the IL
   lowering), or Notes with the reason it stays Unknown (it throws for some argument).
3. A call to each member added has no `threw` flag from IOperation and from IL.
4. The differential gate's rules 1 to 3 hold on the PR and nightly budgets under both lowerings,
   with rule 2's exemption unchanged (open calls only).

## Tests
- one row per member added, in `ClosedCallsTests.ACallHasAThrewFlagUnlessItsMemberNeverThrows`

## Out of scope
Open calls. A member's result. Changing the taint rule.

## Notes
- Filed 2026-10-11 by P2-081, whose ADR names it.
