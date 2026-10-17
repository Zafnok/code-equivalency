# P2-237 Precision audit with execution: the three migration pairs in `full --execute`, P2-212's undetermined results re-adjudicated by replay
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-212

## Goal
P2-212's verdict (`docs/runs/2026-10-15-precision-verdict.md`) adjudicated 136 sampled Divergent results on
`gitextensions-8522`, `duplicati-3124` and `openra-17989`: 1 confirmed, 45 false positive, 90 undetermined. 57 of the
90 are "a real difference by hand trace, not executed" (18 EQ002, 39 EQ006): P2-130's method calls a result confirmed
only with an executed oracle, and the box had .NET 6.0 and 10.0 only, so no `full --execute` run was possible. The
README's "Divergent precision" row for migrations, 2.2% (1 of 46), therefore counts only what a hand trace can refute.
If the 57 confirm, migration precision is about 56% (58 of 103); the verdict's own ceiling is 66.9%.

The user approved installing the .NET 5 runtime on this machine on 2026-10-17, on the condition that it is not a
security risk to have installed. Install it for these runs only, run the three migration pairs with `--execute` so ADR
0035 decision 2 replays every solver Divergent on both runtimes, re-adjudicate P2-212's undetermined results by what was
executed, and uninstall it at the end. This moves the precision row more than every open one-to-two-result precision
ticket together.

## Spec references
`.claude/skills/equiv-corpus-run`, `.claude/skills/equiv-scoreboard`, ADR 0028, ADR 0035 (decision 2 and its
2026-09-28 clarification: `not-applicable` for an EQ006 whose replay shows equal outcomes), ADR 0040 decision 3,
`docs/runs/2026-10-15-precision-verdict.md` (its method, its sample and its appendix), tickets P2-130 and P2-212 (their
criteria and Notes).

## Acceptance criteria (all must hold; nothing beyond them)
1. The runtime. Before any run, `dotnet --list-runtimes` is checked against each migration pair's
   `run.properties.runtimes` (from P2-212's SARIF or a fresh run). Only the .NET 5 runtime is installed: the
   `Microsoft.NETCore.App` 5.0 runtime installer from Microsoft's official download page
   (`dotnet.microsoft.com/download/dotnet/5.0`), its SHA512 checked against the one that page publishes, installed side
   by side with the runtimes already there. Not the SDK, not the ASP.NET Core hosting bundle. .NET Framework 4.8 is part
   of Windows and is not installed. If a side needs a framework the runtime installer does not carry (for example
   `Microsoft.WindowsDesktop.App` 5.0), stop and ask the user before installing anything else; without it, record
   "execution unavailable: <runtime> not installed" for that side and continue. ADR 0040 decision 3 never substitutes
   another runtime. The verdict records the exact version installed and the `dotnet --list-runtimes` output before and
   after.
2. `gitextensions-8522`, `duplicati-3124` and `openra-17989` are each run in `full` mode with `--execute`, compare mode
   quick, `--jobs 4`, through `equiv-corpus-run`, one after the other, at one commit of `main`. Each has a
   `docs/runs/<date>-full-execute-<slug>/SUMMARY.md` that records `run.properties.mode` and the detected runtimes of both
   sides. No run has a lowering notification, an entry in `run.properties.unverified` or exit 5; if one does, record the
   procedure identity, file or reopen the crash ticket, and write the verdict from the pairs that completed.
3. Every solver Divergent (EQ002) in the three runs carries `properties.replay` (`reproduced`, `not-reproduced`,
   `not-constructible`). The verdict counts them per pair, and the EQ006 replay outcomes (`not-applicable` included) per
   pair.
4. Every undetermined result in P2-212's appendix (90) is re-adjudicated, matched by procedure identity and rule as
   P2-212 matched P2-130's. An EQ002 whose replay is `reproduced` is confirmed. For each EQ006 the verdict says whether
   execution can confirm it (for example by running the runtime row's witness input through the body on both runtimes)
   and why or why not, and confirms only those that were executed and differed. A result nothing executed stays
   undetermined, with its obstacle; a result no longer Divergent at the run's commit is listed with what it became and
   counted apart.
5. Each `not-reproduced` replay is a soundness or modelling finding and gets a new `P2-nnn` ticket (ADR 0035 decision 2),
   unless an open ticket already owns its cause.
6. `docs/runs/<date>-precision-execute-verdict.md` gives migration Divergent precision with execution, per pair and over
   the three, next to P2-212's 2.2% (1 of 46), and of the 57 "real difference by hand trace, not executed": how many were
   confirmed, refuted (false positive) or are still undetermined, split EQ002 and EQ006. Its appendix lists every
   re-adjudicated result with its replay outcome and new classification.
7. `equiv-scoreboard` is applied: each README row whose value changed is rewritten, and no other line of `README.md` is.
   The pull request body lists the rows that moved, old value and new.
8. The last step uninstalls the .NET 5 runtime (Apps and Features, or the installer's uninstall) unless the user says to
   keep it, and the verdict records `dotnet --list-runtimes` after it. .NET 5 is out of support and gets no security
   fixes, so it is used for these runs only and nothing else is run on it.

## Files
`docs/runs/`, `README.md`, `docs/ROADMAP.md`, new ticket files (criterion 5). Nothing under `src/`, `tests/` or
`tools/`.

## Tests
None.

## Size guard
A change under `src/`, `tests/` or `tools/`, a fourth pair, or a new sample draw means the ticket was misread: file what
you found.

## Out of scope
The upgrade and cleanup pairs. A fresh sample (this is P2-212's sample, executed). Thorough mode, `seeded` and
`seeded-mech` runs. ADR 0035 decision 3's `differentialTesting` on Unknowns beyond what `--execute` writes anyway: it is
not adjudicated here. Installing the .NET 5 SDK, the ASP.NET Core hosting bundle or any other runtime without asking.
Fixing anything a run finds. A README edit to a row whose value did not change.

## Notes
- Filed 2026-10-17, after the user approved installing the .NET 5 runtime for these runs on the condition that it is
  not a security risk to keep installed; hence install from Microsoft only, runtime only, uninstall at the end.
- Last recorded wall-clock at `--jobs 4` without `--execute` (P2-212, 2026-10-15): 363 s, 447 s and 217 s. Replay adds
  a build and a run of each Divergent on both sides. Never stop a run in progress.
