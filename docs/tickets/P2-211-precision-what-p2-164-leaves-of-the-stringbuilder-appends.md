# P2-211 Precision: the two `StringBuilder` append results P2-164 leaves on `gitextensions-9860`
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-164

## Goal
P2-164 lowers a handler-bound `StringBuilder.Append($"...")` or `AppendLine($"...")` to the call of the `string`
member with the joined parts, where no hole can tell the handler's piece-by-piece writes from one write
(`docs/tickets/done/P2-164-*.md`, VERIFICATION-MODEL section 3). By reading the source, two of the results P2-124
found on `gitextensions-9860` (`docs/runs/2026-10-12-upgrade-verdict.md`) are not taken:

- `GitCommands.Patches.PatchManager::ToPatch(...)`, EQ002: `result.Append($"\n--\n{Application.ProductName} {AppSettings.ProductVersion}")`.
  The holes are static getters and the builder is a local made by `new()`, but it is handed to `chunk.ToPatch(subChunkToPatch, result)`
  before the append, so code a hole runs might reach it.
- `GitCommandsTests.Git.GitBlameCommitTests::TestToString()`, EQ006 in thorough mode from the IL pass, under the
  row `System.Text.StringBuilder::AppendLine(ref `: `str.AppendLine($"Author date: {authorTime}")` and the same with
  `committerTime`, two `DateTime` holes, which P2-086 and P2-139 do not cover, so the handler's calls stay, the pair
  stays Unknown and the IL pass fires the row. No hole touches the builder.

First rerun the pair in compare mode, quick and thorough, to confirm which of P2-124's five EQ002 and the one EQ006 are left.
Then make what is left Equivalent where it is, or record why it is not.

## Spec references
VERIFICATION-MODEL section 3 (the interpolated string and `StringBuilder` paragraphs), ADR 0049, ADR 0040 (runtime
rows), `docs/tickets/done/P2-164-*.md`, `docs/tickets/done/P2-139-*.md` (its table: "A `DateTime` hole", no owner).

## Acceptance criteria (all must hold; nothing beyond them)
1. The rerun's counts, before any code, in `## Notes`: P2-124's five EQ002 and one EQ006, each now Equivalent, Unknown or still Divergent.
2. Each one still Divergent is Equivalent, or `## Notes` says why it cannot be with no new assumption, decided with `equiv-decide`.

## Tests
A unit test per shape taken; the oracle where a lowering changes.

## Out of scope
Format and alignment clauses. Holes of other types than the one the rerun points at.

## Notes
- Filed 2026-10-15 by P2-164, from reading the modern side's source (`37797ea4dd74`), not from a rerun.
