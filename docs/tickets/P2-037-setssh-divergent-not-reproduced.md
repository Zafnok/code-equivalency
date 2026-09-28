# P2-037 A solver Divergent whose counterexample the real runtimes do not reproduce (`SetSsh`)
Status: in-progress
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
- Observed: the corpus pair's two bodies (upstream `GitCommands/Git/GitSshHelpers.cs` at 3f4ed21998af and
  5190ba5c1a5f) differ in one call: legacy `if (!Strings.IsNullOrEmpty(path))`, where `GitExtUtils.Strings.IsNullOrEmpty`
  is the solution's own one-line wrapper over `string.IsNullOrEmpty`; modern `if (!string.IsNullOrEmpty(path))`. Both
  then call `Environment.SetEnvironmentVariable("GIT_SSH", path, EnvironmentVariableTarget.Process)`.
- Observed: the standalone repro (`SetSshDivergenceTests`) gives Divergent with the legacy trace starting
  `Strings::IsNullOrEmpty(string)`, a first event the modern trace does not have, the legacy outcome
  `threw System.Exception` (the wrapper's `threw` edge) and the modern outcome `returned`. That is the corpus shape.
- Decision: the replay is wrong, not the model. The traces differ at their first event, which is a real observable
  (ADR 0018): the legacy build really does call the wrapper and the modern one does not, and a catalogue entry (ADR 0020)
  is the only thing that would equate them. After the split, a call's result or `threw` answer is the solver's free choice,
  which ADR 0026 ("Why") accepts because the divergence is already real. So the model's outcome difference is not a claim
  about the CLR, and M4-009's replay, which observes only outcomes, read the trace divergence as an outcome one and
  reported `not-reproduced`. Nor is every unmodelled callee's `threw` edge asymmetric between sides (the size guard):
  both sides share call functions, and here only the legacy side made the call at all.
- Decision: the fix is in replay, and keeps its precision. A plan whose model's call traces differ still runs, since
  differing real outcomes reproduce the divergence whatever the model chose. Equal real outcomes then give
  `not-constructible` with the reason `the call traces differ, and the model's outcomes rest on call answers chosen after
  they split, which replay does not observe`, instead of `not-reproduced`. The reason travels as
  `ReplayPlan.AlikeReason` (Core contract, init-only, null by default), set by `ReplayDriverFactory` and honoured by
  `Replayer`. Rejected: making every trace-split model not constructible up front, which would lose `reproduced` on
  pairs like `Log(); return 1;` against `return 2;`.
- Decision: the factory compares the two traces by raw call identity. It has no call-identity map (that lives in
  `Equiv.Verify.Z3`'s `TraceEncoder`), so a callee the map renames counts as a split. That errs towards
  `not-constructible`, which keeps `not-reproduced` meaning the model was wrong.
- Decision: criterion 2 does not apply (the model is not wrong), so the standalone repro is kept as an integration test
  that pins the model's shape, and the regression tests are on the replay side (`ReplayerTests`,
  `ReplayDriverFactoryTests`).
