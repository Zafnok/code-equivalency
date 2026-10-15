# P2-209 An `abstraction` Unknown whose sides first differ at a lambda names the runtime-changed member the lambda calls
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-195

## Goal
P2-195 names, in `properties.runtimeChangedCall`, the runtime-changed call or side-specific pure function at
which an `abstraction` Unknown's two sides first answer differently. On its reruns (Notes of
`docs/tickets/done/P2-195-*.md`) 70 of the 297 `runtime-rule` Unknowns it named give a `delegate:<fingerprint>#<site>`
function: a lambda or method group whose fragment calls a runtime-changed member, which the lowering makes
side-specific (`IrPure.RuntimeSensitive` from the fragment's own `RuntimeSensitive`). The name is the model's, but
a reviewer reads a hash, and the member that is the cause is named only in `notCongruent` (most often
`String.StartsWith(string)`, `String.Equals(string,StringComparison)`, `String.EndsWith(string)` and
`Path.Combine(string,string)`). By pair: Git Extensions 41, OpenRA 25, Duplicati 4, plus one Duplicati Unknown
whose body is not congruent by `callee`.

When done, such an Unknown also says which runtime-changed member or members the lambda calls.

## Spec references
VERIFICATION-MODEL section 6 (`runtimeChangedCall`), ADR 0024 (fragments), ADR 0040, ticket P2-067
(`delegate:` functions), `docs/tickets/done/P2-195-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `runtimeChangedCall` that is a side-specific `delegate:` function comes with the runtime-changed members its
   fragment calls, decided with `equiv-decide` and documented in VERIFICATION-MODEL section 6.
2. Rerun the three migration pairs in compare mode quick; Notes give the members now named for the 70, top 10.

## Tests
A unit test with a lambda that calls a runtime-changed member and feeds a shared function; the SARIF snapshot.

## Size guard
Changing a verdict: stop. This is a report, not a decision.

## Out of scope
Deciding the Unknowns. `delegate:` functions that are not side-specific.

## Notes
- Filed 2026-10-15 by P2-195 (criterion 2).
