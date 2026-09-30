# P2-064 Every run ends with a short review list: flagged results grouped by cause, most certain first
Status: todo
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
More than 12 files under `src/` and `tests/` together, or any change to how a verdict is decided:
stop, you are re-deciding verdicts, not grouping them.

## Out of scope
Changing any verdict or rule id. An HTML or Markdown report file (ADR 0006). Adjudicating whether a
group is a true or false alarm (P2-047). Suppressing groups in `equiv.config.json`.

## Notes
- Found by the 2026-09-30 goal review: on Git Extensions the tool leaves about 1,085 flagged results
  in no order, so the "narrow the lens for a human" goal is not met however good the verdicts get.
