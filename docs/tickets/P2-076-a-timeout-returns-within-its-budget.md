# P2-076 A rung that reports a timeout returns within its budget, and the contracts pass is a logged phase
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: none

## Goal
The 2026-09-29 `full` run of `gitextensions-8522` (P2-046) took 8h57m. Six pairs took 5.4 of its 6.8
verify hours, and every one ended Unknown. Their `rung=bounded` lines report `result=timeout` against
a 5000 ms timeout after 126.5, 52.7, 25.6, 16.0, 12.8 and 9.7 minutes
(`GitUI.CommandsDialogs.FormRemotes::InitializeComponent()` is the 126.5). So the budget the rung
reports is not the budget it spends. The time goes somewhere in building the product encoding,
`Z3Backend.Inline`'s substitution, the tactic pipeline or the check, and nobody has measured which.
Separately, after `verify done` at +6h50m the run spent 2h07m in the ADR 0036 contracts pass
(`CompareCommand.WithContracts`), which writes rung lines but no phase or items, so the progress log
cannot show it. Find where the time goes, make a rung that times out return within a stated
multiple of its budget, and log the contracts pass as its own phase. Every verdict stays as it was.
The pairs this ticket speeds up already end Unknown(timeout).

## Spec references
ADR 0029 decision 5 (timeouts are per pair), ADR 0038 (phases and items), ADR 0036 (contracts pass),
`src/Equiv.Verify.Z3/Z3Backend.cs` (`Query`, `Inline`), `src/Equiv.Verify.Z3/LoopLadder.cs` (rung timing),
`src/Equiv.Cli/CompareCommand.cs` (`Verified`, `WithContracts`). P2-050 (deterministic `rlimit`) is
complementary. It bounds the solver, not the work around it.

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first. For the 30 pairs over 30 s in that run's `progress.log`, record in `## Notes` how
   long each spends in encoding, `Inline`, tactic preprocessing, `Check`, and anything else inside the
   rung (for example `ReachableOpaques` or failure refinement). Identities and seconds only. Log a
   `Decision:` line naming the dominant cause.
2. Every rung that ends `timeout` returns within 3 x `timeoutMs` plus the time to encode the pair
   once. A test in `tests/Equiv.Verify.Z3.Tests` shows this on an IR pair built to exceed the budget
   in the stage criterion 1 names. Encoding time is reported on its own in the rung's debug line
   (`encode=…s`).
3. The contracts pass is a `contracts` phase in the progress log (ADR 0038): one item per pair it
   re-verifies, bounded like `verify`. `./tools/corpus/corpus.ps1 -Progress -Summary` lists it.
4. A `full` rerun of `gitextensions-8522` (`equiv-corpus-run`) gives the same rule id for every
   result as P2-046's run, except pairs whose P2-046 result was Unknown(timeout). Those may become
   another verdict, and a list in `docs/runs/<date>-timeout-bound.md` names each one. The report also
   gives the verify and contracts phase seconds, before and after.

## Files
`src/Equiv.Verify.Z3/` (the stage criterion 1 names, and the rung line), `src/Equiv.Cli/CompareCommand.cs`
(`WithContracts` phase), their tests, `docs/runs/<date>-timeout-bound.md`.

## Tests
`LadderFixtureTests.TimeoutReturnsWithinItsBudget` (or the test class of the stage criterion 1 names),
`CompareCommandProgressTests.ContractsPassIsAPhase`.

## Size guard
If criterion 1 shows the time is in Z3 ignoring its own `timeout` inside a tactic, the fix is to
interrupt it (`Context.Interrupt` on a timer) or to bound it with `rlimit`. If that needs P2-050's
`rlimit` plumbing, stop and say so in `## Notes`; do not do P2-050 here.

## Out of scope
Raising or lowering the default `timeoutMs` (P2-050). Verifying pairs in parallel (P2-077). Making the
six pairs decidable.

## Notes
- Found 2026-09-30 while P1-018's runs were in progress, from the 2026-09-29 `full` run's
  `progress.log` (`.corpus/`, not committed). Pairs over 60 s: 18, of which 2 decided (the slowest
  Divergent took 72 s, the slowest Equivalent 28 s). Pairs over 120 s: 11, none decided.
