# P2-037 A solver Divergent whose counterexample the real runtimes do not reproduce (`SetSsh`)
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-009

## Goal
M4-007's `--execute` run on Git Extensions replayed every Divergent on the two real runtimes
(ADR 0035; M4-009). Of the solver-derived Divergents (`EQ002`), one came back
`replay: not-reproduced`: both real runtimes gave the same outcome (`returned` on each), while
the model claimed the legacy side throws. Per `docs/VERIFICATION-MODEL.md`: "The model and the CLR
disagree; in a corpus run that is a soundness or modelling finding and gets a ticket."

Procedure identity: `GitCommands.GitSshHelpers::SetSsh(string)`.

The model's legacy trace starts with `GitExtUtils.Strings::IsNullOrEmpty(string)` and ends in
`threw "System.Exception"`; the real legacy run returned. A `System.Exception` thrown in the model
usually stands for "some call may throw" (an unmodelled callee's `threw` edge, ADR 0018), not a
concrete exception, so the most likely reading is that the model let an *unmodelled* callee throw
on the legacy side only, and the modern side's equivalent callee did not get the same freedom. Rule
that in or out first. Reproduce standalone (a `static void SetSsh(string s)` that calls a helper
with an environment-variable write, legacy calling it through a different helper than modern).

## Spec references
ADR 0018 (call trace), ADR 0035 decisions 1 and 2, `docs/VERIFICATION-MODEL.md` section on replay,
M4-009's replay driver.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide which is wrong, the model or the replay (a driver that cannot see the divergence still
   reports `not-reproduced`, not `not-constructible`), and record the finding as a `Decision:`.
2. If the model is wrong, fix it so the pair is Equivalent or Unknown, never a false Divergent, with
   a regression test from the standalone repro.
3. If the replay driver is wrong, fix it (or make it answer `not-constructible` with a reason) so a
   `not-reproduced` result always means the model was wrong.

## Size guard
One pair. If the cause is that every unmodelled callee's `threw` edge is treated asymmetrically
between sides, stop and write an ADR.

## Out of scope
The 15 EQ006 `not-reproduced` results (P2-038).

## Notes
