# Public migration corpus (ADR 0028)

What `equiv` is measured against: public code only, so every result can be reproduced. Run it
through the skill `.claude/skills/equiv-corpus-run`, not by hand.

| File | What it is |
|---|---|
| `poly-migrationbench-dotnet.csv` | Verbatim copy of Amazon's Poly-MigrationBench .NET list: 100 MIT or Apache-2.0 .NET Framework repos, each pinned at a commit where the build and unit tests pass. The columns are `repo,base_commit,license,num_cs_files,root_sln_or_csproj_files,verify_command`. |
| `pairs.csv` | Public before-and-after migrations pinned by commit. Kind `human`: a person migrated it. Kind `tool`: raw output of .NET Upgrade Assistant or AWS Porting Assistant. |
| `corpus.ps1` | Lists, selects, fetches and cleans pairs; computes ADR 0028's unchanged share; refreshes the upstream list; `-SeedMechanical` drives the seeder (ticket M4-010); `-RuntimeDiff` runs `tools/runtime-diff` on a census's `externalCallees` (ADR 0035, ticket M3-033). |
| `migration-prompt.md` | The fixed prompt an agent gets when it migrates an agent pair. |
| `seeds.md` | The catalogue of hand-written behaviour changes injected in `seeded` mode, used to measure recall. |
| `seeder/` | Console tool (ticket M4-010) that applies M0-012's mutation operators to real methods on the modern side, at scale, by Roslyn syntax rewriting (`Equiv.TestSupport`'s `PairGen` shares its implementation for the differential soundness gate). `-SeedMechanical` copies the modern side to `.corpus/pairs/<slug>/seeded-mech/`, runs it, and writes `seeds.json` there. |

## Nothing third-party is committed

`corpus.ps1` writes only under `<repo>/.corpus/`, which `.gitignore` excludes. It throws before
fetching anything if that stops being true. Checkouts, agent migrations, seeded copies (including
mechanical ones under `.corpus/pairs/<slug>/seeded-mech/`, ticket M4-010) and raw SARIF all live
there. The only files a run commits are `docs/runs/<date>-<mode>-<slug>/SUMMARY.md`, and those hold
numbers, reason names and procedure identities, never source text (see `docs/runs/README.md`); a
mechanical seed's manifest (`seeded-mech/seeds.json`) quotes the seeded method's identity, file and
line from third-party code, so it stays in `.corpus/` too, same as `seeds.json` for the hand-written
seeds. Several pairs are GPL or LGPL (Git Extensions, OpenRA, Duplicati at that commit). Cloning and
analysing them locally is fine. Copying their code into this BUSL repository is not.

## Provenance and regeneration of the Poly-MigrationBench list

- Upstream: <https://github.com/amazon-science/Poly-MigrationBench>, file
  `Poly-MigrationBench-dotnet.csv`.
- Pinned upstream commit: `b0a91412d64a14b7fd61319591bd4b00a574f4c2` (2025-11-18). Copied
  2026-09-23. SHA-256 of the copy is
  `9756d034856d356cb977aa8024a0fae99017db95a2377c53817f6316dabb64a7` (`.gitattributes` keeps its
  bytes as fetched).
- The list is reproducible at any time: `./tools/corpus/corpus.ps1 -Refresh -UpstreamRef
  b0a91412d64a14b7fd61319591bd4b00a574f4c2` must report `added 0, removed 0, base_commit changed 0`.
- To move to a newer upstream: `./tools/corpus/corpus.ps1 -Refresh` (dry run against `main`), then
  `-Refresh -Apply -UpstreamRef <sha>`. Update the commit and hash above in the same PR. A changed
  list changes which repos `-Select` picks, so a summary written before the refresh stays tied to
  the old commit; its SUMMARY.md records the commit.
- Paper: MigrationBench, arXiv:2505.09569.

### Attribution

`poly-migrationbench-dotnet.csv` is from Poly-MigrationBench, licensed under the Apache License 2.0
(a copy is at `LICENSES/Apache-2.0.txt`). Its NOTICE reads:

> Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.

It is used unmodified. The repositories it lists keep their own licences (MIT or Apache-2.0, per
its `license` column), and none of their code is in this repository.

## `pairs.csv` sources

| Slug | Source | Why it is here |
|---|---|---|
| `gitextensions-8522` | gitextensions/gitextensions PR #8522 | Large WinForms app, 4.8 to .NET 5, no new features. Of 175 modified `.cs` files, 121 change by at most 4 lines. It includes real behaviour edits (`Comparer<string>.Default` to `StringComparer.Ordinal`, `?? string.Empty`). The solution has `.vcxproj` and `.wixproj` projects, so it needs M3-024. |
| `duplicati-3124` | duplicati/duplicati PR #3124 | Framework to .NET 5, a first step of 100 commits. Mostly project-file churn plus deleted Mono-only code. |
| `openra-17989` | OpenRA/OpenRA PR #17989 | Minimal: 4 `.cs` files changed, mostly a new assembly loader. |
| `eshop-manual`, `eshop-upgrade-assistant`, `eshop-porting-assistant` | mjrousos/UpgradeSample | One legacy MVC 5 app next to a hand-finished .NET 6 port and to the raw output of two migration tools. |

`-Unchanged` matches files by relative path. A migration that renames folders (`eshop-manual`
renames `eShopLegacyMVC` to `eShop.MVC`) therefore scores 0% even where contents match. Read
the unchanged share for such pairs with that in mind, and prefer the census's `pairsCongruent`
(M3-015).

## `-RuntimeDiff` (ADR 0035, ticket M3-033)

`./tools/corpus/corpus.ps1 -RuntimeDiff <slug> [-Top 200]` reads the most recent `-lower-only`
census SARIF under `.corpus/pairs/<slug>/runs/*census*/equiv.sarif`, takes the union of both
sides' top `-Top` `loweringCensus.externalCallees` (already sorted by call-site count), and runs
`tools/runtime-diff --member` on each distinct member. Reports land under
`.corpus/runs/<slug>/runtime-diff/<member>.json`, one file per member; nothing is written outside
`.corpus/`. A member whose identity carries an equiv-only `<T1,T2>` generic instantiation suffix
has it stripped first, since `runtime-diff` resolves a member against real Roslyn symbols and
knows nothing of that suffix. Run a census first (`equiv compare --lower-only`) if the command
reports no census SARIF.
