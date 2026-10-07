# P2-135 Precision: thorough mode's IL pass reports a Divergent where the two sides only bind to different callees
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-032

## Goal
ADR 0049 runs the IL lowering in thorough mode, the default, as a pass over the pairs that are still Unknown. A
pair whose IOperation bodies hold an unshared opaque is then verified from IL, where the construct is a sequence of
calls. When the two runtimes compile the same source to different callees, the two IL bodies have different call
traces and the pass answers Divergent. `samples/business-layer`'s `OrderService.Describe` is the checked-in
instance: an interpolated string that binds `String::Format` on .NET Framework 4.8 and the
`DefaultInterpolatedStringHandler` members on .NET 10. Its true verdict is Equivalent, quick mode reports Unknown,
and thorough reports EQ002 with `decidedBy: il-pass`.

ADR 0039's measurement already saw this (`docs/runs/2026-10-01-il-fallback-verdicts.md`: 21 of 1,294 changed pairs
of `gitextensions-8522` moved from Unknown to Divergent, none reproduced by replay). P1-032 made it the default's
behaviour; its Notes give the count from the thorough run. When done, a Divergent the IL pass produces is one a
user can trust as much as one the first pass produces, or it is not reported as a Divergent.

## Spec references
ADR 0049 (decisions 2, 5 and 7), ADR 0039, ADR 0020 (the API-equivalence catalogue), ADR 0026, ADR 0035 decision 2,
VERIFICATION-MODEL.md sections 3.1 and 6, `docs/runs/2026-10-01-il-fallback-verdicts.md`, ticket P1-032's Notes,
`src/Equiv.Cli/CompareCommand.cs` (`LaterPasses`, `Standing`), `samples/business-layer/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Every `decidedBy: il-pass` EQ002 of P1-032's thorough run on `gitextensions-8522` is adjudicated as
   `docs/runs/2026-09-30-divergent-audit.md` does: confirmed, or a false positive with its cause. Identities and
   classifications only, in `docs/runs/<date>-il-pass-divergents.md`.
2. From that table, one of these is chosen by `equiv-decide` and logged as a `Decision:` line, with the count of
   pairs each would have changed: (a) the IL lowering applies the API-equivalence catalogue as the IOperation
   lowering does, so a binding-only difference is the same call on both sides; (b) a Divergent from the IL pass
   stands only when its counterexample also diverges through the IOperation bodies' modelled part, and is otherwise
   the earlier Unknown; (c) another rule the audit points to. A rule that changes which of two results stands is a
   change to ADR 0049 decision 2 and goes through `equiv-adr` first.
3. `samples/business-layer`'s `Describe` is not EQ002 in thorough mode, and `samples/unknown-new-throw`'s `Width`,
   a real divergence the IL pass finds, still is.
4. No result that is Equivalent or Divergent in quick mode changes on any sample (P1-032's
   `QuickDecisionsHoldInThorough` still passes).

## Files
`src/Equiv.Cli/CompareCommand.cs` or `src/Equiv.Frontend.CSharp/Lowering/Il/`, their tests,
`samples/business-layer/README.md` and `expected.sarif.json`, `docs/VERIFICATION-MODEL.md`.

## Tests
`IlPass_BindingOnlyDifference_IsNotDivergent`, `IlPass_RealDivergence_StaysDivergent`, the two samples' snapshots.

## Size guard
If the audit finds more than one unrelated cause, fix the one with the most pairs and file a ticket per other cause.

## Out of scope
Turning the IL pass off in thorough (ADR 0049 decides that). The lambda bodies the IL lowering does not read
(P2-079).

## Notes
- Filed by P1-032 on 2026-10-05, from `samples/business-layer` under the new default.
- P1-032's thorough run of `gitextensions-8522` (2026-10-06) has 12 `decidedBy: il-pass` Divergents, 5 EQ002 and 7
  EQ006, and 1 `il-pass` Equivalent. Its SARIF is `.corpus/pairs/gitextensions-8522/runs/20261006-1003-full-thorough-capped/`
  in that ticket's worktree; a fresh thorough run reproduces it.
