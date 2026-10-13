# P2-171 A call of a local function is not an opaque when both sides declare the same local function
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-127

## Goal
P2-127 closed a false-Equivalent path: a call of a local function was a call of a shared name, so
a proof never read the local function's body. Since then the call is an opaque with reason
`LocalFunction`, shared only when the pair is congruent. P2-127's Notes name the cost: "a member
that calls a local function by name is Unknown even when the local function is the same on both
sides", and leave proving those to a later ticket. No open ticket is that one.

P2-110's quick run at equiv `78853202` measures the cost on a cleanup
(`docs/runs/2026-10-13-cleanup-gitextensions-11372/SUMMARY.md`): on `gitextensions-11372`
`LocalFunction` is in the reason set of 43 of 319 changed pairs (13.5%), the whole reason set of
20, and the first reason of 21 Unknowns. It is the largest opaque reason on that pair, and one of
the two things that take its solver-proved share from 75.2% on P2-120's branch to 68.0%. On
`gitextensions-11284` it is in 18 of 178 reason sets (10.1%). The pull requests rewrite a
collection initializer or a `var`; most of these members' local functions are untouched.

When done, a member that calls a local function is decided when the two sides' local functions can
be shown to be the same procedure or equivalent ones, and is still never Equivalent on a proof that
did not read them.

## Spec references
`docs/tickets/done/P2-127-*.md` (the Notes: the decision, the cost and the 26 lost Equivalents);
ADR 0018 (callees and congruence); ADR 0024 (fingerprints); VERIFICATION-MODEL sections 3 and 5;
`IOPERATION-COVERAGE.md` (where it says what a call of a local function lowers to); `.claude/skills/equiv-decide` and `equiv-adr` (what a
shared callee is allowed to rest on is close to an ADR's subject: run the bar test first).

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over `gitextensions-11372`'s 43 changed pairs with `LocalFunction` in the reason
   set, in how many the local functions the member calls have the same text on both sides, in how
   many they differ, and what they capture. Record the table in Notes.
2. Decide, with `equiv-decide` and the `equiv-adr` bar test, how a call of a local function is
   compared: as a matched pair of procedures with their captures as parameters, by a fingerprint of
   the local function's lowered body, or by inlining. Log the decision and why the P2-127 path
   stays closed under it.
3. A sample whose two sides declare the same local function and differ elsewhere in the member is
   Equivalent by a `proofMethod` other than `congruence`.
4. P2-127's samples stay as they are: a sample whose two local functions differ in behaviour is
   Divergent or Unknown, never Equivalent.
5. Rerun `gitextensions-11372` in compare mode quick. Notes record the reason-set count next to 43
   and the solver-proved count next to 217 of 319, and no pair is Equivalent whose local functions
   differ in text without a proof that read them.

## Tests
- `IrLowererTests`: a call of a local function under the new rule; P2-127's
  `ACallOfALocalFunctionIsNotACallOfASharedName` still holds or is replaced by its stronger form.
- An integration test per sample of criteria 3 and 4.
- A property test (CsCheck) that a change inside a local function's body is never reported
  Equivalent for the member that calls it unless the two bodies are.

## Size guard
Recursive local functions, or local functions that capture by reference a variable the member
writes after the call, may stay opaque: say which in Notes and stop there.

## Out of scope
Lambdas and method groups (`DelegateCreation`, P2-136's subject); static local functions that the
compiler already treats as ordinary methods need no special case beyond the rule decided here.

## Notes
- Filed 2026-10-13 from P2-110's rerun of the cleanup pairs. No open ticket owned the reason.
- Soundness comes first here: this ticket undoes part of what a soundness fix cost, so criterion 4
  and the property test are not optional.
