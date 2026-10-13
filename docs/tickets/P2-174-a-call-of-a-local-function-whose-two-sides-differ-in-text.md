# P2-174 A call of a local function is decided when the two sides' local functions differ in text
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-171

## Goal
P2-171 names a call of a local function by the fingerprint of the bound code it runs (ADR 0024,
clarified 2026-10-13). One function called on both sides is then one call, and the solver decides
the member. Two functions that differ in any bound operation are two opaques, and the pair is
Unknown(Opaque): the proof would have to read both bodies, and nothing lowers a local function's
body.

P2-171's quick run of `gitextensions-11372` at its branch measures what that leaves. Of the 44
changed pairs that call a local function, 13 still name `LocalFunction` as an opaque:
- in 12 the pull request rewrote an expression inside the local function (a collection initializer
  to a collection expression), so the two functions differ in text;
- in 1 (`GitCommands.GitModule::ParseGitBlame(string,System.Text.Encoding)`) the function is the
  same on both sides and writes a local it captures, which a fragment has no output for.

Two more pairs whose functions differ are Unknown for another first reason (one `timeout`, one
`opaque` that does not name the call), so 14 of the 44 have functions that differ.

When done, a member whose two local functions differ in text is decided by a proof that read both
bodies, and one whose function writes a captured local or has a `ref` or `out` argument is decided
when the function is the same on both sides.

## Spec references
`docs/tickets/done/P2-171-*.md` (the Notes: the count, the decisions and what each left out); ADR
0024's clarification of 2026-10-13 (its "Not chosen" paragraph names inlining); ADR 0018;
VERIFICATION-MODEL sections 3 and 5; `IOPERATION-COVERAGE.md` row `Invocation`;
`.claude/skills/equiv-decide`, `equiv-adr` and `equiv-extend-ir`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over the 14 pairs of `gitextensions-11372` whose local functions differ, what the
   differing functions hold that the lowering leaves opaque today (a lambda, a collection
   expression with a spread, an iterator, an `await`), and how many are free of all of it. Record
   the table in Notes. If fewer than 5 are free of it, say so and stop: the functions' own opaques
   are then the subject, not the call.
2. Decide, with `equiv-decide` and the `equiv-adr` bar test, how the body of a local function that
   has no shared fingerprint reaches the solver: its control-flow graph lowered at the call, with
   its parameters bound to the arguments and its captures the member's own variables, or a
   procedure of its own with the captures as parameters and outputs. A call whose fingerprint both
   sides share stays the one call P2-171 made it.
3. A sample whose two sides declare local functions that differ in text and compute the same value
   is Equivalent by a `proofMethod` other than `congruence`; one whose functions compute different
   values is Divergent or Unknown.
4. A sample whose function writes a captured local, the same on both sides, with a change
   elsewhere in the member, is Equivalent; so is one with an `out` argument.
5. P2-171's and P2-127's samples and property test stay as they are.
6. Rerun `gitextensions-11372` in compare mode quick. Notes record the results that name
   `LocalFunction` next to 13 and the solver-proved count next to 234 of 319, and no Divergent
   appears that a hand trace does not confirm.

## Tests
- `LocalFunctionCallLoweringTests`: a call under the new rule; a recursive function and one called
  from two sites.
- `LocalFunctionCallTests` (Integration): criteria 3 and 4, and the property test extended to
  bodies that differ in text and compute the same value.
- `LoweringOracleTests` covers a body lowered at its call.

## Size guard
A recursive local function, an iterator and an `async` local function may keep the opaque they
have: say which in Notes and stop there.

## Out of scope
Lambdas and method groups (`DelegateCreation`); the IL pass; a capture the member stores after a
shared call, unless criterion 1 finds it in these pairs.

## Notes
- Filed 2026-10-13 from P2-171's rerun of `gitextensions-11372`. No open ticket owned what it left.
- Soundness comes first, as in P2-171: a body lowered at its call reads and writes the member's own
  variables, so every write must reach the member's SSA, and a function that a lambda keeps alive
  after the call is not one to lower there.
