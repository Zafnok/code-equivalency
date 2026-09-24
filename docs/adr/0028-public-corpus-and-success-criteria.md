# ADR 0028: Success is measured on a public corpus, against criteria fixed before the first run

Status: accepted (2026-09-23); superseded in part by 0034 (the lowerable-share row is evaluated on changed pairs)

## Context
M3-022 and M4-007 were written to run on an unnamed "real 4.8/10 pair". No such pair was
identified, and results on private code could not be reproduced or reviewed in this public repo
anyway. So M3-022 had no input, and the feasibility question it was meant to answer (ADR 0027) had
no date.
The pivot thresholds from the 2026-09-23 feasibility review existed only in chat. Public data does
exist:
- Amazon's Poly-MigrationBench (Apache-2.0) lists 100 MIT or Apache-2.0 .NET Framework repos. Each
  is pinned at a commit where the build and unit tests pass. It has no migrated side.
- Human migrations that add no feature. Git Extensions PR #8522 (4.8 to .NET 5): of 175 modified
  `.cs` files, 121 change by at most 4 lines. Also Duplicati PR #3124, OpenRA PR #17989, and
  mjrousos/UpgradeSample (eShop as net472 and net6 folders, plus the raw Upgrade Assistant and
  Porting Assistant output).

## Decision
1. **The corpus is public and pinned.** `tools/corpus/` holds two checked-in manifests:
   - `poly-migrationbench-dotnet.csv`, a verbatim copy of the upstream file with its commit recorded
     in `tools/corpus/README.md`. `corpus.ps1 -Refresh` regenerates it.
   - `pairs.csv`, which pins each public before-and-after migration by a legacy commit and a
     modern commit (solution paths included).
2. **No third-party code enters git.** `corpus.ps1` clones only into `.corpus/`, which is
   git-ignored, and raw SARIF stays there too. The only file committed per run is
   `docs/runs/<date>-<slug>/SUMMARY.md`. It holds counts, reason names and procedure identities,
   and no source text, snippet or counterexample value. Nothing from the corpus is pushed, forked
   or published.
3. **There are three kinds of pair.** A *human pair* is a public before and after, and its diff is
   the ground truth for "what a real migration changes". A *tool pair* is the same legacy code next
   to a migration tool's raw output (.NET Upgrade Assistant, AWS Porting Assistant). An *agent
   pair* is a Poly-MigrationBench base commit plus a migration an agent makes in `.corpus/`. The agent follows the checked-in
   prompt `tools/corpus/migration-prompt.md`, and the run records the model. That is the
   production use case: an agent migrates the code and `equiv` decides whether to trust it.
4. **The skill `equiv-corpus-run` is the only way to run the corpus.** It has three modes:
   - `census` (`--lower-only`, needs M3-014 and M3-024);
   - `full` (needs M3-004);
   - `seeded`: `full` plus behaviour changes from `tools/corpus/seeds.md` injected into the modern
     side.
5. **Success criteria, fixed now, before any data exists.** They are evaluated on the Git
   Extensions pair and on the median of at least three agent pairs:

   | Metric | Definition | Rule |
   |---|---|---|
   | Unchanged share | Before M3-015: share of legacy `.cs` lines in files the migration left byte-identical (`git diff --numstat`). After it: `pairsCongruent / matchedPairs`. | Unchanged share below 40%: stop. The migrations are rewrites, not ports, so this is the wrong problem class. Write a new ADR before any more engine work. |
   | Lowerable share | `pairsWithoutOpaque / matchedPairs` | An M4 ticket stays in M4 only if the census shows it unlocking at least 5% of matched pairs. If the top three cannot together lift the lowerable share to 15%, solver precision stops after M3. The product is then congruence, EQ006 and the catalogue, and the remaining M4 tickets go to the backlog. |
   | Project load rate | C# projects loaded / C# projects in the solution (M3-024) | Below 100% on a human pair is a ticket, never a known limitation. |
   | Line-scoped Unknown share | Share of Unknown results with `scope: line` (ADR 0029) | Reported every run. The `method` share must fall with every M4 ticket. |
   | Seeded recall | Seeded changes reported as Divergent, or as Unknown with a cause on the seeded line | Must be 100%. A seeded change reported Equivalent is a soundness bug and preempts all other work. |

   Changing a threshold takes a new ADR, so the rules cannot be fitted to the data after the fact.

## Why
- The census is the cheapest feasibility evidence there is (ADR 0027), and without a runnable
  input it would never run.
- An agent pair on a benchmark repo is the real use case, and anyone can reproduce it. A human
  pair shows what a careful person changes. Git Extensions' "no functional change" migration
  swapped `Comparer<string>.Default` for `StringComparer.Ordinal` and added `?? string.Empty`,
  which are exactly the edits `equiv` exists to flag.
- Poly-MigrationBench's verify command checks that the build and tests pass. It cannot see a
  behaviour change the tests miss, which is the gap `equiv` fills. The repos are MIT or
  Apache-2.0, and the manifest itself is Apache-2.0, so a verbatim copy with attribution is
  allowed.
- Thresholds written after seeing the numbers would be chosen to pass.

## Rejected
- **A private pair.** Its results cannot be reproduced or reviewed here, and a commit or SARIF
  upload of it would publish code this repository has no right to. The corpus answers the same
  question.
- **Vendoring corpus repos as git submodules or copies.** That redistributes third-party code,
  some of it GPL (Git Extensions, OpenRA) or LGPL (Duplicati at that commit), inside a BUSL
  repository.
- **Committing raw SARIF.** It carries file paths, spans and counterexample values from
  third-party code, and the summary needs none of them.
- **Only linking to the upstream CSV.** The upstream file can change or disappear, and a pinned
  copy keeps old runs reproducible.
- **Poly-MigrationBench's pass/fail as the metric.** Passing tests is necessary, not sufficient.

## Consequences
- M3-022 runs on the corpus in `census` mode, and M4-007 runs in `full` and `seeded` modes.
- New: `.gitignore` entry `.corpus/`; `tools/corpus/` (manifests, `corpus.ps1`, the migration
  prompt, seeds and a README with the Apache-2.0 attribution); `docs/runs/README.md`; skill
  `.claude/skills/equiv-corpus-run`.
- The corpus needs Windows with VS Build Tools, because the legacy side loads through
  MSBuildWorkspace (ADR 0004). Some corpus repos will not restore. The skill skips them and
  records the reason.
- The first census may stop the project (unchanged share below 40%). That outcome is the point of measuring first.

## Clarifications
- 2026-09-24 (P2-013). "C# projects in the solution" means the projects the solution's default
  configuration builds (a `Build.0` entry for `Debug|Any CPU`, else the first configuration
  listed). A project the solution does not build is not part of the product: it is neither loaded
  nor skipped, and is listed in `run.properties.projectsNotBuilt` instead (VERIFICATION-MODEL.md).
