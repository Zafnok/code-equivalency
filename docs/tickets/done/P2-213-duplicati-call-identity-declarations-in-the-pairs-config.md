# P2-213 Duplicati's replaced callees are declared in the pair's committed config, so a default corpus run uses P2-192
Status: done (PR #511)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-192 (done, ADR 0059) lets a user declare in the config's `callIdentityRenames` that a legacy member and its
modern replacement are one call, and reports every Equivalent that rests on the declaration
(`unprovenAssumptions`, `properties.callIdentitiesMapped`). Its own rerun of `duplicati-3124` passed a config
kept under `.corpus/` and never committed, with three entries: the remoting `CallContext::LogicalGetData(string)`
and `::LogicalSetData(string,object)` against the solution's `CallContext::GetData(string)` and
`::SetData(string,object)`, and `Duplicati.Library.Utility.HashAlgorithmHelper::Create(string)` against
`System.Security.Cryptography.HashAlgorithm::Create(string)`. With it, 8 of P2-130's 11 false EQ002 became EQ001.

`corpus.ps1 -Compare` passes `tools/corpus/configs/<slug>.json` when it exists (P2-197), and only
`openra-17989.json` does. So P2-212's default run of `duplicati-3124` (`docs/runs/2026-10-15-precision-verdict.md`)
has all 11 of P2-192's results still EQ002, and the 9 of them in its sample are false positives whose cause has a
done fix: 9 of the 26 false positives in Duplicati's sample, the pair's largest cause.

When done, the pair's committed config declares the three entries, and a default corpus run of `duplicati-3124`
reports them as P2-192 did.

## Spec references
ADR 0059, `docs/tickets/done/P2-192-*.md` (Notes: the three entries and their run),
`docs/tickets/done/P2-197-*.md` (the committed pair config and its README source), `tools/corpus/README.md`
"Pair configs", `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/corpus/configs/duplicati-3124.json` holds the three `callIdentityRenames` entries, and
   `tools/corpus/README.md` "Pair configs" gives each one's source: the pull request's replacement of the member,
   one for one, as P2-192's Notes traced it.
2. A `tools/corpus/tests` case asserts the file's entries, as `PairConfig_OpenRaStatesItsModsRunOnNet5` does for
   OpenRA's.
3. `duplicati-3124` rerun in compare mode quick, `--jobs 4`; Notes list P2-192's 11 procedures with their new
   results, next to P2-192's table.

## Tests
The `tools/corpus/tests` case.

## Out of scope
New entries beyond the three. The AWS SDK call (P2-205) and the renamed static getter (P2-206).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
- Whether a corpus run may hold a declaration (it is the user's unproven assumption, ADR 0059) is the first thing
  to settle through `equiv-adr`'s bar test: P2-197's config states a fact about the build (a runtime), this one a
  claim about behaviour. Every result that rests on it names it, so the scoreboard's Equivalent rows would carry
  the assumption visibly.
- `equiv-adr`'s bar test: ADR 0059 already decides what a `callIdentityRenames` entry means and how a result names it. Its
  Consequences line "Corpus runs carry no config, so the scoreboard does not move" described `main` before P2-197 gave
  pairs a committed config, and is not part of the Decision. Committing a declaration is applying the Decision to a case
  it did not spell out, so the vehicle is a dated `## Clarifications` bullet in ADR 0059, not a new ADR. The bullet keeps
  the Decision as written: each entry stays an unproven assumption that every result resting on it names, the scoreboard
  counts such an Equivalent as it counts any Equivalent with a non-empty `unprovenAssumptions` (ADR 0019), and an entry
  is committed only where the pair's pull request itself replaces the calls one for one, with the files cited in
  `tools/corpus/README.md` "Pair configs".
- Criterion 1: `tools/corpus/configs/duplicati-3124.json` holds the three entries P2-192's config held, byte for byte in
  its keys and values. README "Pair configs" gives one row per entry. The sources, read from the two checkouts: the 4
  `LogicalGetData` and 6 `LogicalSetData` calls on the legacy side are 4 `CallContext.GetData` and 6 `CallContext.SetData`
  calls on the modern side, file for file, in `LocalizationContext.cs`, `LocalizationService.cs`, `Log.cs` and
  `CallContextSettings.cs`. The legacy `HashAlgorithmHelper.cs` (a forwarder to FasterHashing's `FasterHash.Create`) is
  deleted, and its 24 calls in 16 files are `HashAlgorithm.Create` calls with the same argument in the same files.
- Criterion 2: `PairConfig_DuplicatiDeclaresTheCalleesItsPullRequestReplaces` in `tools/corpus/tests/Compare.Tests.ps1`
  asserts the three entries, that there are no others, and that the config states nothing else. `Compare.Tests.ps1`:
  9/9 pass under Pester 5.
- Decision: the test asserts the entries exactly and that the file holds only `callIdentityRenames`, so a fourth entry
  or a stated runtime needs its own source and test, as P2-197's OpenRA case does for `runtimes.legacy`.
- Criterion 3: `duplicati-3124`, compare mode quick, `--jobs 4`, on this branch (P2-212's branch, `main` `bfbf413e` plus
  docs, plus this change; no `src/` change since `bfbf413e`), `./tools/corpus/corpus.ps1 -Compare duplicati-3124 -Mode
  quick -Tag p2-213 -CompareArgs '--jobs', '4'`. The script passed `--config tools/corpus/configs/duplicati-3124.json`
  itself. Exit 1, 468 s (lower 38 s, verify 411 s). "P2-212" is P2-212's default run of the same commit with no config
  (`20261015-1718-full-quick`). "P2-192" is P2-192's run with the uncommitted config. Row numbers are P2-130's sample
  rows, as in P2-192's table.

  | row | procedure | P2-212 | P2-192 | this run | unproven assumption |
  |---|---|---|---|---|---|
  | 0 | `LocalizationContext::Dispose()` | EQ002 | EQ001 bounded | EQ001 bounded | `LogicalSetData` |
  | 3 | `LocalizationContext::.ctor(CultureInfo)` | EQ002 | EQ002 | EQ002 | `LogicalGetData`, `LogicalSetData` |
  | 8 | `CallContextSettings`1.ContextGuard::Dispose()` | EQ002 | EQ001 bounded | EQ001 bounded | `LogicalSetData` |
  | 11 | `CallContextSettings`1::get_ContextID()` | EQ002 | EQ001 bounded | EQ001 bounded | `LogicalGetData` |
  | 13 | `CallContextSettings`1.ContextGuard::.ctor()` | EQ002 | EQ001 bounded | EQ001 bounded | `LogicalSetData` |
  | 14 | `LocalizationService::get_Current()` | EQ002 | EQ001 bounded | EQ001 bounded | `LogicalGetData` |
  | 23 | `Log::set_CurrentScope(LogScope)` | EQ002 | EQ001 bounded | EQ001 bounded | `LogicalSetData` |
  | 2 | `Utility::CalculateHash(Stream)` | EQ002 | EQ002 | EQ002 | `HashAlgorithmHelper::Create` |
  | 7 | `BackendManager::CalculateFileHash(Stream)` | EQ002 | EQ001 bounded | EQ001 bounded | `HashAlgorithmHelper::Create` |
  | 16 | `BackendManager::GetFileHasherStream(...)` | EQ002 | EQ001 bounded | EQ001 bounded | `HashAlgorithmHelper::Create` |
  | 20 | `S3IAM::CanCreateUser(string,string)` | EQ002 | EQ002 | EQ002 | none (P2-205) |
  | 12 | `HashCalculatingStream::.ctor(Stream,string)` (undetermined) | EQ002 | EQ001 bounded | EQ001 bounded | `HashAlgorithmHelper::Create` |
  | 22 | `Metahash::.ctor(...)` (undetermined) | EQ002 | EQ001 bounded | EQ001 bounded | `HashAlgorithmHelper::Create` |

  Every row matches P2-192's run. 8 of P2-192's 11 false EQ002 are EQ001, and the two undetermined rows are too. Each
  names its declared callee in `unprovenAssumptions` and `callIdentitiesMapped`. The three still Divergent are P2-192's
  three: row 3 is a real procedure-level difference, row 2 is P2-206, row 20 is P2-205.
  Whole run against P2-212's: 6,486 results in both. 14 results moved, all to EQ001 and all carrying
  `callIdentitiesMapped`: the 10 EQ002 above and 4 EQ003. Nothing moved the other way. EQ002 23 to 13, EQ001 5,838 to
  5,852, EQ003 322 to 318. 27 results carry `callIdentitiesMapped` (14 EQ001, 9 EQ003, 2 EQ006, 2 EQ002), as in
  P2-192's run.
  No new finding, so this PR files no ticket. The scoreboard is not edited: this PR adds no file under `docs/runs/`, and
  the next audit's run of the pair picks the config up.
- Toolchain: `.corpus/` is per worktree, and this worktree had no checkout. Its `.corpus` was a junction to P2-212's
  worktree's `.corpus`, whose `pair.json` already names that worktree's two checkouts. No checkout was fetched or
  changed. Pester 5 is not installed on the box (only Windows' built-in 3.4.0). It was saved under the session's
  scratch directory for the test run.
