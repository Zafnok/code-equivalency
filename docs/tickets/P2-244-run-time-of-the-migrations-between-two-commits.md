# P2-244 Measurement: the migrations ran 25% to 34% longer at `de93b9c7` than at `bfbf413e`
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-237

## Goal
P2-237's runs of the three migration pairs (equiv `de93b9c7`, quick, `--jobs 4`, `--execute`) took 488 s, 563 s and 272 s,
against P2-212's 363 s, 447 s and 217 s at `bfbf413e` on the same box. The `execute` phase is 1 to 9 s; the rest is in
`lower` (+61%, +24%, +43%) and `verify` (+20%, +23%, +16%), on all three pairs, `openra-17989` included, whose every count
is unchanged. The scoreboard's run-time row moved from mean 5 min (3 to 7) to mean 6 min (3 to 9). It is not known whether
the commits between the two (P2-207 to P2-234) or the box's load caused it.

When done, the cause is known: either a commit and the phase it slowed, or "the box", with numbers.

## Spec references
`.claude/skills/equiv-corpus-run` (section 5, `-Progress -Summary`), `docs/runs/2026-10-17-precision-execute-verdict.md`,
`docs/runs/2026-10-15-precision-verdict.md`, `docs/runs/2026-10-09-jobs-scaling.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `openra-17989` and `gitextensions-8522` are run in `full` mode, quick, `--jobs 4`, without `--execute`, at `bfbf413e`
   and at the current `main`, alternating, twice each, on a box that runs nothing else, and each run's phase table is
   recorded.
2. If the difference holds, the commit range is bisected on `openra-17989`'s `lower` phase, and the commit and the
   procedures whose lowering took longest are named in a `docs/runs/` measurement file; a regression gets its ticket.
3. If it does not hold, the measurement file says so, and `equiv-scoreboard` is applied to it.

## Tests
None.

## Size guard
A change under `src/` means the ticket was misread: file what you found.

## Out of scope
Fixing a slow pass.

## Notes
- Filed 2026-10-17 by P2-237.
