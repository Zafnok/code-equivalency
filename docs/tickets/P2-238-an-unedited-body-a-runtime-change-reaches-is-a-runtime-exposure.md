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
- Decision: the mapping lives in the reporter (`VerdictRule.Describe(VerificationResult, ...)`, a `ResultRule`), read
  from the result's `notCongruent`; nothing in `Equiv.Verify.*` or the frontend changed, so the size guard held. Every
  pass keeps `notCongruent`, so quick, thorough and the IL pass map alike.
- Decision: an exposure's rules are resolved for `runtimeExposure` as follows. A callee, field or event is looked up in
  the table inside the pair's interval with no arguments, as the frontend's flag was, and named by the row's `member`;
  `extern:<id>` by its `marshalling` entry; `float-to-integer` has `changedIn` net9.0 and the .NET 9 JIT
  breaking-change page; `x87` has no `changedIn`, since it turns on the platform and can apply on one .NET Framework
  version (VERIFICATION-MODEL section 3), and the C# specification's types page, which says floating point may run at a
  higher precision; `extern` has neither, since no page lists the interop changes it stands for (ADR 0054 decision 4).
  A rule nothing resolves is listed by its name alone. A key is left out when it has no value.
- Decision: `helpUri` is the row the witness's call matched, else the first rule that has a page. An exposure the real
  runtimes showed keeps `runtimeExposure` as EQ006, so a reviewer still reads that its body is unedited.
- Decision: messages are `<identity>: unedited body; the runtime change between <older> and <newer> reaches it through
  <rule> (<url>), ...`, then `. Witness: <model>`, `. Whether it reaches the outcome is unknown (<reason>): <detail>`
  (plus a line-scoped Unknown's claim), or `, and the real runtimes show it: <model or observation>` on an EQ006.
- Decision: six tiers in SARIF's 0 to 100 need a narrower tier: `16 x (6 - tier) + min(count, 7999) / 500`. Tiers 1 and
  6 keep their ranks; EQ002 moves from 60 to 64, EQ006 from 40 to 48, a line-scoped EQ003 from 20 to 16, EQ007 is 32.
- Decision: `equiv mcp`'s summary counts EQ007 as `Runtime exposure n` after Unknown, so its Divergent and Unknown
  counts keep meaning EQ002 + EQ006 and EQ003; `corpus.ps1 -Metrics` prints the EQ007 split (witnessed, and the rest by
  `unknownReason`), which the SUMMARY template asks for.
