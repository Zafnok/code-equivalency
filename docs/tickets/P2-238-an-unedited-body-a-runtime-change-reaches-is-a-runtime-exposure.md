# P2-238 An unedited body a runtime change reaches is reported as a runtime exposure (EQ007)
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-212

## Goal
ADR 0060 decisions 1 to 5 and 7. A matched pair whose `notCongruent.component` is `runtime-rule` (equal texts, out of
congruence only by a runtime rule) is analysed as today, and its outcome is reported as EQ007 `RuntimeExposure` instead
of EQ006 or EQ003: with `properties.model` where the solver found the input on which the runtime-changed call decides
the outcome, and with `properties.unknownReason` where it did not. Over P2-212's five large pairs that is 879 results
(259 EQ006 and 620 EQ003 today); the one Equivalent stays EQ001. No exit code changes.

When done, a reviewer reads "unedited body; the runtime change reaches it through <member>" on those pairs, Divergent
counts only results on edited bodies, and the README scoreboard has a Runtime exposure row measured on a rerun.

## Spec references
`docs/adr/0060-*.md` (the decision and its projected scoreboard), ADR 0040 decision 2, ADR 0058 decision 4, ADR 0035
decisions 2 and 3, ADR 0019; VERIFICATION-MODEL sections 3 ("Runtime-changed APIs"), 6 (the verdict table,
`notCongruent`, the review list tiers) and 7; ticket P2-173 (`notCongruent`).

## Acceptance criteria (all must hold; nothing beyond them)
1. The EQ007 rule descriptor exists (`kind: fail`, `level: warning`), and a `runtime-rule` pair's result is EQ001,
   EQ007 with `properties.model`, or EQ007 with `properties.unknownReason`, as ADR 0060 decision 2 says, in both compare
   modes and the IL pass. An `extern` function a rule reaches is EQ007, not EQ003 `no-body`. An EQ002 on such a pair
   stays EQ002.
2. `properties.runtimeExposure` lists one entry per rule `notCongruent` names, legacy side first, with the rule,
   `changedIn`, `url` and the row's `precondition` name when it has one; the message names the rules and the interval.
3. Under `--execute`, `replay: reproduced` or an observed differential-testing difference makes the result EQ006 with
   `proofMethod: observed`; equal outcomes leave it EQ007.
4. Exit codes: a witnessed EQ007 counts as a Divergent, an unwitnessed one as an Unknown. A snapshot or integration test
   shows a run whose exit code is the same before and after.
5. EQ007 is a flagged result with review group key `runtime-change:<member>` and the tier between EQ006 and EQ003;
   `run.properties.reviewList`'s counts include it.
6. VERIFICATION-MODEL sections 1, 3, 6 and 7, README's exit-code text, ARCHITECTURE.md's exit codes, the
   `equiv-corpus-run` SUMMARY template (EQ007 counted, split witnessed and not) and the `equiv-scoreboard` row table
   (a Runtime exposure row; Divergent is EQ002 + EQ006; Left for a reviewer adds EQ007) say what ADR 0060 decides. ADR
   0060 is accepted, and ADR 0040, 0054 and 0058's Status lines name it.
7. Rerun the five large pairs in compare mode quick, `--jobs 4`. Notes give each pair's EQ007 (witnessed, not), EQ006,
   EQ002 and Unknown next to ADR 0060's projected table, and explain any difference. Re-split P2-212's adjudicated
   sample by the new rule ids (fingerprints are stable) and give migration Divergent precision over EQ002 and EQ006
   only, and the exposures' own count of confirmed and false positive. Apply `equiv-scoreboard`, adding the row.
8. The commit that changes the SARIF carries `Release: minor`.

## Tests
Unit tests for the outcome mapping (each of the four cases) and the message; a Verify snapshot of a sample pair with an
unedited body calling `Path.Combine` across net48 and net5.0, with and without a solver model; the review-list tier and
rank; the exit-code mapping; an `extern` pair under ADR 0058's `everyImport` row.

## Size guard
If the outcome mapping needs a change in `Equiv.Verify.*` (the solver's query or encoding), stop: ADR 0060 changes only
the report.

## Out of scope
Decision 6 (P2-239). Any row's reach: the open valid-path and row tickets (P2-218, P2-222, P2-223, P2-229, P2-236 and
kin) stay as they are.

## Notes
- Filed 2026-10-17 with ADR 0060, from P2-212's "Per pair" section.
