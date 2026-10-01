# P2-064 Every run ends with a short review list: flagged results grouped by cause, most certain first
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-046, P2-062

## Goal
The product's promise on a migration is to prove what it can and narrow what is left to a list a
human can review. On Git Extensions (P2-046) it leaves about 1,085 flagged results: 84 EQ002, 275
EQ006 and 726 EQ003, each reported per method with no order. Nobody reviews 1,085 methods. A
migration repeats the same few edits across many methods, so most of these share a cause: 275 EQ006
results name at most 206 distinct runtime-changed members, and 194 Unknowns are opaque only because
of `DelegateCreation`. Group every flagged result by its cause, rank the groups so the most certain
findings come first, and write the list into the SARIF run and to stdout. A reviewer then reads
tens of groups, not a thousand methods. The ticket is one grouping rule, one ranking rule, where
they are written, and a sample that shows it.

## Spec references
ADR 0006 (SARIF only; extra data goes in `properties` bags), ADR 0026 (Divergent only when
untainted), ADR 0029 (Unknown scope), ADR 0033 (the MCP tool result is the SARIF log plus a short
summary), ADR 0035 (`proofMethod: observed`, `replay`), `docs/VERIFICATION-MODEL.md` section 6.

## Design
- **Group key.** Every result with ruleId EQ002, EQ003 or EQ006 gets exactly one key, a string
  derived from data the result already carries. Nothing is recomputed from source.
  - EQ006: the runtime-changed member the message cites (the `RuntimeChange` that
    `SarifReportWriter.MessageText` receives). One group per member.
  - EQ002: the call identities that differ between the two sides' traces in the counterexample,
    sorted and joined. This is the "the migration swapped A for B" case. A counterexample whose
    traces call the same members groups by `proofMethod` alone.
  - EQ003: `unknownReason`, plus for `opaque` and `abstraction` the sorted opaque reasons behind it
    (P2-062 makes those available). Example: `abstraction:DelegateCreation`.
  The exact encoding is the implementer's decision (`equiv-decide`); the key must be stable across
  runs of the same inputs so that baselines can compare groups.
- **Rank.** Each group gets a SARIF `rank` (0 to 100, higher is looked at first). Every result in
  the group gets the same rank. The order, highest first:
  1. Divergent that the real runtimes showed (`proofMethod: observed`, or `replay: reproduced`);
  2. other EQ002;
  3. EQ006;
  4. EQ003 with `scope: line`;
  5. EQ003 with `scope: method`.
  Within a tier, a larger group ranks higher. The numeric mapping is the implementer's decision.
- **Where it goes.**
  - Each result: `properties.reviewGroup` (the key) and `rank`.
  - The run: `run.properties.reviewList`, an array ordered by rank. Each entry has `group`,
    `ruleId`, `rank`, `count`, and the first five `identities` in the group.
  - `equiv compare` stdout: after the existing "analysed lines of code" line, one line
    `review list: <G> groups for <R> flagged results`, then the top 10 groups, one line each.
    With `mcp`, the same lines go into the tool's short summary (ADR 0033), not onto stdout.
- **Pitfalls.**
  - Congruent and Equivalent results get no group.
  - EQ004 and EQ005 are not flagged results here.
  - A baseline run (`baselineState`) groups only `new` and `updated` results in stdout, but
    `run.properties.reviewList` counts all of them.
  - `rank` must be written through Sarif.Sdk's `Result.Rank`. Read `Sarif.xml` for its default
    (`equiv-package-api`), and never write it by hand into the property bag.

## Acceptance criteria (all must hold; nothing beyond them)
1. The design goes through `equiv-adr`'s bar test before any code: a new result property, a run
   property and a ranking rule under ADR 0006. The outcome is a `Decision:` line in Notes, and if the
   bar test asks for it, a clarification on ADR 0006.
2. Every EQ002, EQ003 and EQ006 result in an `equiv compare` log carries `properties.reviewGroup`
   and a `rank` by the rules above. No EQ001, EQ004 or EQ005 result carries either.
3. `run.properties.reviewList` is written on every run that is not `--lower-only`, ordered by
   rank, with `group`, `ruleId`, `rank`, `count` and at most five `identities` per entry. Its counts
   sum to the number of EQ002, EQ003 and EQ006 results.
4. `equiv compare` prints the `review list:` line and at most ten group lines to stdout. `equiv mcp`
   returns the same lines in its summary and writes nothing extra to stdout.
5. A new sample `samples/repeated-edit/` holds one legacy/modern pair where the same API swap is
   made in three methods and one unrelated method changes on its own. Its `expected.sarif.json`
   shows the three swapped methods in one group, ranked above the other group.
6. `docs/VERIFICATION-MODEL.md` section 6 documents `reviewGroup`, `rank` and `reviewList`.
7. `.claude/skills/equiv-corpus-run/SKILL.md`'s SUMMARY template gains a "Review list" line: groups,
   flagged results, flagged results as a share of matched pairs, and the top five groups by count.
   No run is required by this ticket; the next corpus run fills it.

## Files
`src/Equiv.Core/Reporting/ReviewList.cs` (new), `src/Equiv.Core/Reporting/SarifReportWriter.cs`,
`src/Equiv.Cli/CompareCommand.cs`, the MCP tool's summary in `src/Equiv.Cli/Mcp/` or
`src/Equiv.Cli/EquivTools.cs`, `samples/repeated-edit/` (new), `docs/VERIFICATION-MODEL.md`,
`.claude/skills/equiv-corpus-run/SKILL.md`, and `docs/adr/0006-output-and-surface.md` only if
criterion 1 says so. Existing samples' `expected.sarif.json` files change only by the new properties.

## Tests
- `tests/Equiv.Core.Tests`: `ReviewListTests.Eq006_groups_by_runtime_changed_member`,
  `ReviewListTests.Eq002_groups_by_differing_callees`,
  `ReviewListTests.Eq003_groups_by_reason_and_opaque_reasons`,
  `ReviewListTests.Observed_divergent_ranks_above_eq002_above_eq006_above_unknown`,
  `ReviewListTests.Larger_group_ranks_higher_within_a_tier`,
  `ReviewListTests.Equivalent_and_added_removed_get_no_group`,
  `ReviewListTests.Key_is_stable_across_result_order` (CsCheck property test).
- `tests/Equiv.Cli.Tests`: `CompareCommandTests.Prints_review_list_after_line_counts`,
  and an MCP test that the summary holds the review list lines and stdout does not.
- `tests/Equiv.Tests.Integration`: the `repeated-edit` sample's snapshot (Verify).

## Size guard
More than 12 hand-written files under `src/` and `tests/` together (regenerated snapshots are not
counted; see the Deviation in Notes), or any change to how a verdict is decided: stop, you are
re-deciding verdicts, not grouping them.

## Out of scope
Changing any verdict or rule id. An HTML or Markdown report file (ADR 0006). Adjudicating whether a
group is a true or false alarm (P2-047). Suppressing groups in `equiv.config.json`.

## Notes
- Found by the 2026-09-30 goal review: on Git Extensions the tool leaves about 1,085 flagged results
  in no order, so the "narrow the lens for a human" goal is not met however good the verdicts get.
- Decision: `equiv-adr` bar test (criterion 1) -> first row: a clarification on ADR 0006, no new ADR.
  `reviewGroup` and `reviewList` are extra data in `properties` bags, which ADR 0006 already decides,
  and `rank` is SARIF 2.1.0's own result property (section 3.27.25), so no result schema is invented and
  no verdict, rule id, level, fingerprint or exit code changes. Alternatives: a new ADR (row 4, "the
  SARIF shape"), ticket Notes only. Rule: the bar test's "take the first row that fits".
- Decision: key encoding -> `runtime-change:<row member>` (EQ006), `calls:<a>|<b>` (EQ002, the sorted
  identities that only one side's trace calls), else `proofMethod:observed` or `proofMethod:none` (a
  solver's Divergent carries no `proofMethod`), and `<unknownReason>[:<r1>+<r2>]` (EQ003, as
  `changedReasonSets` joins reasons). Alternatives: a hash, a JSON object. Rule: 3 (a snapshot pins it).
- Decision: an `opaque` Unknown's reasons are its causes' reasons, an `abstraction` Unknown's are its
  opaque fragments' `reason` (P2-062); an `IrPure` abstraction adds nothing to the key. Rule: 1.
- Decision: a group is (rule id, key), and it takes the best tier any of its results is in, because
  every result in a group has one rank while `replay` and `scope` are per result. Alternatives: put
  the tier in the key, which would split one cause into two groups. Rule: 4.
- Decision: `rank = 20 x (5 - tier) + min(count, 9999) / 500`, so tier 1 is 80.002 to 99.998 and tier 5
  is 0.002 to 19.998. It depends only on the group's tier and size, never on the other groups, so it is
  the same across runs. Alternatives: position in the run's list, `count / (count + 1)`. Rule: 3.
- Decision: grouping and ranking read the SARIF results, after the baseline's carry-overs are added,
  so an `absent` or unverified carry-over is counted like any other result (criterion 3's sum). It
  keeps the `reviewGroup` it was written with; one from a baseline older than this ticket gets
  `ungrouped`, since its key cannot be derived from the SARIF alone. Rule: 4.
- Decision: `SarifReportWriter.Write` takes `reviewList` (default false, so the writer's other tests
  keep their run as it was) and the CLI passes `!LowerOnly`; `ReviewList.Lines` reads the lines back
  from the run, for stdout and for the MCP summary, and gives none without the run property.
  Alternatives: the CLI builds the list. Rule: 4.
- Decision: a group line is `  <ruleId> count=<n> rank=<rank> <group>`, the group last because it is
  the long part. Rule: 5.
- Deviation: the Size guard counted every file under `src/` and `tests/`, and criterion 2 cannot be
  met under that count: a `rank` and a `reviewGroup` on every flagged result change every snapshot
  that holds one. The PR touches 11 hand-written files there (5 in `src/`, 6 in `tests/`) and 10
  regenerated snapshots (six `SarifReportWriterTests.*.verified.txt`, two `IlFallbackSampleTests`
  ones, `business-layer.execute.sarif`, `removed-null-check.execute.sarif`), each changed only by the
  new properties or the new stdout lines. No verdict, rule id, fingerprint or exit code changed, which
  is what the guard is for, so its text now counts hand-written files.
- The 15 existing `expected.sarif.json` files differ from before only by `rank`, `reviewGroup` and
  `reviewList`; a script compared each with its `main` version after removing the three, and checked
  on each that exactly the EQ002, EQ003 and EQ006 results carry the two and that the list's counts sum
  to them.
- `README.md` and `docs/ARCHITECTURE.md` said stdout carries two lines at most and the MCP summary
  is one line. Both are corrected here, though neither is in the Files list. The MCP `compare` tool's
  description says "a short summary" for the same reason.
- `--lower-only` still writes `reviewGroup` and `rank` on an `unmatched-overload` Unknown (criterion 2
  is about every log), and no `reviewList` and no stdout lines (criterion 3).
- `samples/business-layer` shows the grouping on an older sample: `RoundTotal`'s group is
  `calls:System.Math::Round(decimal,int)|System.Math::Round(decimal,int,System.MidpointRounding)`.
  `samples/removed-null-check` under `--execute` is `reproduced`, so its group is in tier 1 (80.002).
- Toolchain: Sarif.Sdk's `Result.Rank` defaults to -1.0 (the bundled `sarif-2.1.0.json` schema), and
  the SDK leaves a default out of the JSON, so an unflagged result has no `rank`. Verify's JSON
  snapshots drop an empty collection, so a run whose `reviewList` is `[]` shows no change in a
  `.verified.txt` written through `VerifyJson`.
