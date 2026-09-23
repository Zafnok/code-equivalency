---
name: equiv-corpus-run
description: Assess whether equiv works on real code by running it on the public migration corpus (Poly-MigrationBench .NET repos, Git Extensions and other public before/after pairs), then scoring it against ADR 0028's fixed success criteria. Use when asked to assess project success or feasibility, run the corpus, do the M3-022 census or the M4-007 first real run, test equiv on a real repo, or refresh the corpus list.
---

# Corpus run

Design: `docs/adr/0028-public-corpus-and-success-criteria.md` (what counts as success) and
`docs/adr/0029-blast-radius-is-bounded-at-every-level.md` (the `scope` metric). The mechanics are in
`tools/corpus/corpus.ps1`, and `tools/corpus/README.md` says where every list came from. This
skill is the judgement part. Windows only: legacy projects load through MSBuildWorkspace
(ADR 0004).

## Hard rules

1. **Only corpus code.** Never point `equiv`, this skill or `corpus.ps1` at code that is not listed
   in `tools/corpus/*.csv`. If asked to, stop and say ADR 0028 forbids it.
2. **Nothing third-party enters git.** Checkouts, migrations, seeded copies, SARIF and logs stay
   under `.corpus/`, which git ignores and `corpus.ps1` checks. The only files you commit are
   `docs/runs/**/SUMMARY.md` and `docs/runs/*-verdict.md`, and they follow `docs/runs/README.md`:
   numbers, reason names and identities, never source text or counterexample values. Before
   committing, `git status --porcelain` must list nothing outside `docs/runs/`, `docs/ROADMAP.md`
   and `docs/tickets/`.
3. **Thresholds are fixed.** Apply ADR 0028's table as written. If a result feels wrong, report it.
   Do not reinterpret a threshold. Changing one takes a new ADR.
4. **Record, don't fix.** A crash, a load failure or a wrong verdict is a finding for the summary
   and a ticket. It is never a reason to edit `src/` during a run.

## 1. Which modes are available

Read the `Status:` lines. A mode is available only when every ticket it needs says `done`:

| Mode | Needs | Runs |
|---|---|---|
| `census` | M3-014, M3-024 | `equiv compare --lower-only` |
| `full` | M3-003, M3-004 (and M3-025 for `scope`) | `equiv compare`, and again with `--fail-on unknown` |
| `seeded` | same as `full` | `full` on a copy of the modern side with seeds from `tools/corpus/seeds.md` |

```powershell
Select-String -Path docs/tickets/M3-014-*.md, docs/tickets/M3-024-*.md -Pattern '^Status:'
```

If the mode asked for is not available, say which tickets are missing and stop. A run before
M3-024 aborts with exit 4 on most real solutions, so it says nothing about equiv. It was
observed on 2026-09-23 with `eshop-upgrade-assistant`.

## 2. Choose the pairs

The default set (M3-022 and M4-007 require at least this):
- the human pair `gitextensions-8522`;
- three agent pairs, from `./tools/corpus/corpus.ps1 -Select -Count 3` (rows with `Pick = True`).

When a repo fails at any step below, take the next row in `-Select`'s order, and record the
skipped repo and the reason in the verdict file. `tool` pairs (`eshop-*`) and the other human
pairs are optional extras. Run `./tools/corpus/corpus.ps1 -List` to see them.

## 3. Fetch and prepare

```powershell
./tools/corpus/corpus.ps1 -Fetch gitextensions-8522
./tools/corpus/corpus.ps1 -Fetch <owner/name>          # each agent pair
./tools/corpus/corpus.ps1 -PrepareAgent <owner/name>   # copies legacy to .corpus/pairs/<slug>/modern
```

`.corpus/pairs/<slug>/pair.json` now holds `legacySolution`, `modernSolution` and, for agent
pairs, `verifyCommand`.

Restore both sides before `equiv` sees them. MSBuildWorkspace does not restore, and a legacy
`packages.config` project fails with "references NuGet package(s) that are missing":

```powershell
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild <legacySolution> -t:restore -p:RestorePackagesConfig=true -v:m
dotnet restore <modernSolution>
```

"The reference assemblies for .NETFramework,Version=vX were not found" means a targeting pack is
missing, even when a `vX` folder exists under `Reference Assemblies`. Installing one changes the
machine, so ask the user. Name the Build Tools component, e.g.
`Microsoft.Net.Component.4.6.1.TargetingPack`, and the README's `winget ... --add` form. If they
decline, skip the repo.

## 4. Make the modern side of an agent pair

Spawn one subagent per agent pair, working only in `.corpus/pairs/<slug>/modern`. Its prompt is the
block in `tools/corpus/migration-prompt.md` with `<solution>` and `<verify>` filled in, and
nothing else: no hints about `equiv`, seeds or this skill. Record the agent, the model and the date.
If you use a different migrator (Copilot's upgrade agent, .NET Upgrade Assistant), record that
instead. Afterwards:
- `dotnet build <modernSolution>` must succeed. Otherwise skip the repo and record the reason.
- Run `verifyCommand` on both sides and record pass, fail and skip counts. List every test that
  passes on legacy and fails on modern by name. That is a behaviour change the migration made,
  which is ground truth for M4-007 criterion 4.

## 5. Run equiv

Build once: `dotnet build src/Equiv.Cli -c Release`. Each run gets its own directory,
`.corpus/pairs/<slug>/runs/<yyyymmdd-hhmm>-<mode>/`. After M3-004, use the published `equiv` if
it exists.

```powershell
$p = Get-Content .corpus/pairs/<slug>/pair.json -Raw | ConvertFrom-Json
$run = ".corpus/pairs/<slug>/runs/$(Get-Date -Format yyyyMMdd-HHmm)-census"
New-Item -ItemType Directory -Force $run | Out-Null
$t = Measure-Command {
  dotnet run --project src/Equiv.Cli -c Release --no-build -- compare `
    --legacy $p.legacySolution --modern $p.modernSolution --lower-only --out "$run/equiv.sarif" *> "$run/console.txt"
}
"exit=$LASTEXITCODE seconds=$([int]$t.TotalSeconds)" | Set-Content "$run/exit.txt"
./tools/corpus/corpus.ps1 -Metrics "$run/equiv.sarif"
./tools/corpus/corpus.ps1 -Unchanged <slug>     # unchanged share until M3-015 fills pairsCongruent
```

- `full`: drop `--lower-only`. Run once with defaults and once with `--fail-on unknown` into a
  second SARIF.
- `seeded`: copy `modern` to `modern-seeded`, apply 5 to 10 seeds following `tools/corpus/seeds.md`
  exactly, and record them in `.corpus/pairs/<slug>/seeds.json`. Then run `full` against
  `modern-seeded`. For each seed, find its method's result. It passes when the result is EQ002, or
  EQ003 with a `relatedLocation` on the seeded line. A seed reported EQ001 is a soundness failure:
  report it to the user first.

## 6. Write the summary

One `docs/runs/<yyyy-mm-dd>-<mode>-<slug>/SUMMARY.md` per pair, in exactly this shape. Write `n/a`
where the SARIF has no value yet; `-Metrics` prints `n/a` for those.

```markdown
# <mode> run: <slug>

- Pair: <kind>, <repo>, legacy <sha12>, modern <sha12 or "agent migration">
- Corpus list: Poly-MigrationBench @ <pinned commit from tools/corpus/README.md> (agent pairs only)
- Migrated by: <agent / model / date, or "human" / tool name>
- equiv: <git rev-parse --short HEAD of this repo>, mode <mode>, wall-clock <s>, exit <code>

## Load
- Projects: legacy <loaded>/<total C#>, modern <loaded>/<total C#>; skipped: <name: reason>, ...
- Project load rate: <%>

## Census
| | legacy | modern |
|---|---|---|
| procedures | | |
| analysed lines | | |

- Matched pairs <n>; without opaque <n> (<%>); whole-body opaque <n> (<%>); congruent <n or n/a> (<%>)
- Unchanged share: <%> (<"unchanged files" proxy | pairsCongruent>)
- Lowerable share: <%>

Top opaque reasons (up to 15): | reason | legacy | modern | owning ticket or "none" |

## Verdicts (full and seeded only)
- By rule: EQ001 <n>, EQ002 <n>, EQ003 <n>, EQ004 <n>, EQ005 <n>, EQ006 <n>
- By proofMethod: ...
- Unknown by scope: line <n>, method <n>. Line-scoped Unknown share: <%>
- Top Unknown reasons: ...

## Tests (full only)
- legacy <pass/fail/skip>, modern <pass/fail/skip>
- Passed on legacy, failed on modern: <test name: verdicts of the procedures its stack trace names>

## Seeds (seeded only)
| seed id | procedure identity | verdict | on the seeded line? |
- Seeded recall: <%>

## Findings
One line each, with the ticket it became (`P2-nnn`, or an existing ticket id).
```

## 7. Apply ADR 0028 and hand over

Write `docs/runs/<yyyy-mm-dd>-<mode>-verdict.md`:
- one table row per pair with the five ADR 0028 metrics (unchanged share, lowerable share,
  project load rate, line-scoped Unknown share, seeded recall);
- the medians over the agent pairs;
- the outcome of each ADR 0028 rule for the Git Extensions pair and for the agent median;
- one line: **continue**, **re-scope** or **stop**.

Then do what the ticket in hand says (M3-022: reorder M4 and write `P2` tickets; M4-007: write
tickets for findings). Show the user the verdict line. A **stop** or **re-scope** is theirs to
act on, through a new ADR. Never act on it yourself.

## Housekeeping

- `./tools/corpus/corpus.ps1 -Clean <id>` removes a pair's working files. Checkouts in
  `.corpus/repos/` are shared between pairs and kept.
- To move to a newer Poly-MigrationBench list: `-Refresh` (dry run), then `-Refresh -Apply
  -UpstreamRef <sha>`, and update the pinned commit and hash in `tools/corpus/README.md`. Do this
  only in its own PR, never in the middle of a run, because it changes which repos `-Select` picks.
