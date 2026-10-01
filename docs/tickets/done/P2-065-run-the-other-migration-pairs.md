# P2-065 Run the five migration pairs that have never had a full run
Status: done (PR #322)
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
Every judgement about whether `equiv` works on a real framework-to-Core migration rests on one
human pair, Git Extensions #8522. The three agent pairs are pure retargets and change almost no
code. `tools/corpus/pairs.csv` already pins five more migrations that have never had a `full` run:
- `duplicati-3124` and `openra-17989`, both human;
- `eshop-manual`, a hand-finished .NET 6 port;
- `eshop-upgrade-assistant` and `eshop-porting-assistant`, the raw output of Microsoft's and AWS's
  migration tools on the same app.

The two tool pairs are the closest thing to the product's real use: a tool migrates, and `equiv`
says what to check. Run all five and report them the way P2-046 reports Git Extensions. That gives
the "prove it, then narrow what's left" goal more than one data point. It measures and files tickets
only; it changes no code.

## Spec references
ADR 0028 decisions 1 to 4 (the corpus, and nothing third-party in git), ADR 0034 (changed pairs),
`.claude/skills/equiv-corpus-run/SKILL.md` (section 2 lists these pairs as optional extras),
`docs/runs/2026-09-30-full-verdict.md` (the format to follow).

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the five pairs is fetched and run in the skill's `full` mode, then with `--execute`
   where the runtimes are installed, at one `equiv` commit recorded in every SUMMARY. The runs go
   one after another and never overlap on one checkout (P2-046's void run).
2. Each pair has `docs/runs/<date>-full-<slug>/SUMMARY.md` in the skill's template, including:
   - project load rate;
   - matched and changed pairs;
   - changed pairs proved Equivalent (by `proofMethod`), Divergent (EQ002 and EQ006), and Unknown
     by reason and scope;
   - `runtimeChangeCalls` and package changes;
   - the `--execute` replay counts.
   If P2-064 is done by then, the "Review list" line is filled too.
3. A pair that does not load, or loads below 100%, gets its reason in its SUMMARY. On a human pair
   below 100%, the reason is also a ticket (ADR 0028's load-rate rule). It is never written up as a
   known limitation.
4. `docs/runs/<date>-migrations-verdict.md` has one table with a row per pair, plus Git Extensions
   from P2-046 for comparison. Its columns:
   - changed pairs;
   - share of changed pairs proved Equivalent;
   - share Divergent;
   - share Unknown;
   - flagged results (EQ002 + EQ003 + EQ006) as a share of matched pairs.

   It says in plain words whether Git Extensions was typical. These pairs are extras and take no
   part in ADR 0028's rule table; the verdict file says so.
5. Every pair-level crash, `not-reproduced` replay, and opaque reason at or above 5% of a pair's
   changed pairs with no open owner becomes a `docs/tickets/P2-nnn-*.md` ticket and a ROADMAP line,
   as P2-046 did.
6. No source text, snippet or model value is committed (`docs/runs/README.md`).

## Files
`docs/runs/<date>-full-<slug>/SUMMARY.md` (five), `docs/runs/<date>-migrations-verdict.md`, new
`docs/tickets/P2-nnn-*.md` for findings, `docs/ROADMAP.md`. `tools/corpus/corpus.ps1` only if a
pair cannot be fetched without a fix, and then with the fix named in the verdict file.

## Tests
None, unless `tools/corpus/corpus.ps1` changes; then a `tools/corpus/tests` case for the change.

## Size guard
Any edit under `src/`: stop, that is a finding and becomes a ticket. More than five pairs: stop, a
sixth pair is its own ticket.

## Out of scope
Seeded runs on these pairs. New pairs in `pairs.csv` (P2-058, P2-066). Changing ADR 0028's thresholds.
Adjudicating Divergent results by hand (P2-047).

## Notes
- Found by the 2026-09-30 goal review: the "in-place migration" goal is judged on a single real
  migration.
- Decision: the `--fail-on unknown` run the skill asks for is skipped. `--fail-on` only changes the
  exit code (P2-046 and M4-007 made the same call).
- Decision: a `--lower-only` census was added for `duplicati-3124` and `openra-17989`. Their `full`
  and `--execute` runs crash before the first verdict and write no SARIF, so without it the verdict
  table would have no changed-pair count for the two pairs most like Git Extensions.
- Decision: the run-level crash's stack was captured with a .NET startup hook kept outside the
  repository, in two extra diagnostic reruns. Nothing under `src/` changed (size guard), and P2-082
  has a stack to start from instead of a bare message.
- Decision: `eshop-porting-assistant`'s modern restore fails on NU1605 and was not rerun with the
  warning suppressed. The project has syntax errors, so it is skipped either way, and a restore
  the skill does not describe would not be the raw tool output any more.
- Decision: P2-085 (compare a project that does not compile) is filed although criterion 5 does not
  name load failures on tool pairs. It is the reason both tool pairs say nothing, and they are the
  pairs closest to the product's use.
- Decision: P2-086 to P2-088 are filed by criterion 5 as written. ADR 0039's IL fallback was the named
  owner of `InterpolatedString` and `Binary` in P2-046; P1-018 left it off by default, so they have no
  open owner. `AnonymousObjectCreation` is over 5% only on a 26-pair sample; the ticket says so.
- Deviation: criterion 2's verdict figures are `n/a` for `duplicati-3124` and `openra-17989`; there are
  no verdicts to count (P2-082). "Review list" is `n/a` everywhere: P2-064 is not done.
- Observed: the first `--execute` pass failed on a mistake in the run script (exit 3, a usage error,
  before any load). Those attempts were discarded and the pass rerun.
- Observed: ticket numbers P2-076 to P2-081 were taken on `main` while this ran; the findings start
  at P2-082.
- Result: `docs/runs/2026-10-01-migrations-verdict.md`. None of the five runs finished clean. Git
  Extensions is typical in census shape and is the only migration that completes. Tickets P2-082 to
  P2-088.
