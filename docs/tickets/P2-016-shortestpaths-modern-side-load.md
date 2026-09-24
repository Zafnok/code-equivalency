# P2-016 `adapters-shortest-paths-dotnet`'s modern side loads zero procedures
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
In the M3-031 rerun (2026-09-24), `pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s census
reported `procedures: {legacy: 5, modern: 0}`, `matchedPairs: 0`, `run.properties.unverified` with
320 entries, and exit code 4. `projectsSkipped` was legacy 6 / modern 0: the 6 legacy test
projects fail restore ("Your project file doesn't list 'win' as a RuntimeIdentifier"), which is a
plausible, separate finding, but does not explain why the modern side — 0 projects skipped, same
13-project solution — analysed no procedures at all, or why the legacy side (7 projects that did
restore) produced only 5. M3-022 (2026-09-23, same repo, same commit) reported 100% load rate and
312 matched pairs.

## Spec references
`docs/runs/2026-09-24-census-pmb-tomasjohansson__adapters-shortest-paths-dotnet/SUMMARY.md`;
VERIFICATION-MODEL's `run.properties.unverified` definition; M3-024, P2-013 (project loading).

## Acceptance criteria (all must hold; nothing beyond them)
1. Root-cause why the modern side's procedure count dropped to 0 and the legacy side's to 5,
   despite `projectsSkipped: {legacy: 6, modern: 0}` implying most projects loaded. Compare
   against the environment this ticket was filed from (a fresh worktree's `.corpus/refasm`,
   `NoWarn`, `TargetFrameworkRootPath`) versus M3-022's, since the same repo pinned at the same
   commit scored differently under each.
2. Fix the cause, or, if it is environmental (not a product bug), say so and record what
   isolates it.
3. A rerun of the `census` mode on this pair after the fix matches M3-022's `matchedPairs` order
   of magnitude (not necessarily the exact count, since M3-030 and P2-013 changed what counts).

## Size guard
If the cause is in `corpus.ps1`'s restore or environment setup, fixing it there is in scope (it is
what this ticket is about). If it turns out to be an `Equiv.Frontend.CSharp` loading bug, that is
still in scope, but stop and write a new ADR-bar check first if the fix would touch matching
semantics rather than loading.

## Out of scope
The `win` RuntimeIdentifier restore failure on the 6 legacy test projects — file that as its own
finding if it is not already covered by an existing ticket.

## Notes
- Filed from M3-031's rerun; that ticket recorded the gap (`(no opaque)` census: `changedPairs 0`
  either way, since this is a pure-retarget agent pair) and moved on rather than debugging it, per
  the corpus-run skill's "record, don't fix" rule.
