# P2-107 On a same-runtime pair, a whole-body opaque pair with identical source is not "changed"
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-055, P2-058

## Goal
ADR 0034 counts a pair as changed when it is not congruent, and a whole-body opaque pair is never
congruent. On a migration that is a fair default. On a cleanup pair it hides the result:
`powershell-19687` reports 140 changed pairs, and 128 of them (82 `unbound`, 46 `no-body`) have
the same source text on both sides and no edit from the pull request. The lowerable share reads
4.3% where the 12 edited pairs give 50.0%, and the review list leads with 128 results nobody
needs to review. Decide, through `equiv-adr`, whether such a pair counts as unchanged when both
sides run on the same runtime and the body's syntax and every symbol it binds are identical, and
what verdict it gets.

## Spec references
ADR 0034 (changed pairs), ADR 0040 decision 2 (a same-runtime pair crosses no rule), ADR 0018
(congruence); P2-072 (a pair an audit took for identical source and was not: two files of one name,
only one of them in the solution, which limits how "identical" may be established); `docs/runs/2026-10-02-cleanup-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. If the decision is to count them as unchanged: Notes record `changedPairs` on a rerun of
   `powershell-19687` next to the 140 it has now, and no pair the pull request edited leaves it.
3. A pair that crosses a runtime keeps the counting it has now.
4. The soundness argument is written down: what makes two whole-body opaque bodies the same
   function, and why P2-072's case is not a counterexample.

## Tests
- the unit tests the decision names
- `CensusTests.AnIdenticalOpaqueBodyOnOneRuntimeIsNotAChangedPair`, if the decision is to change

## Out of scope
Line-scoped opaques. Cross-runtime pairs.

## Notes
