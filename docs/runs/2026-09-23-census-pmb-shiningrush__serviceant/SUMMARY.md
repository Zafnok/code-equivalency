# census run: pmb-shiningrush__serviceant

- Pair: agent, ShiningRush/ServiceAnt, legacy e36009c2ee86, modern agent migration (local commit 6d1a2c425c49)
- Corpus list: Poly-MigrationBench @ b0a91412d64a
- Migrated by: Claude Code subagent / Sonnet 5 / 2026-09-23, prompt `tools/corpus/migration-prompt.md`
- equiv: 87b730e, mode census, wall-clock 7 s, exit 0

## Load
- Projects: legacy 6/6, modern 6/6; skipped: none
- Project load rate: 100%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 136 | 137 |
| analysed lines | 1,532 | 1,535 |

- Matched pairs 136; without opaque 30 (22.1%); whole-body opaque 50 (36.8%); congruent n/a (M3-015 not landed; the SARIF's 0 is a placeholder)
- Unchanged share: 100% ("unchanged files" proxy: 34 of 34 legacy `.cs` files, 2,010 of 2,010 lines; the agent changed only project files)
- Lowerable share: 22.1%

Top opaque reasons (up to 15; bodies holding at least one opaque with the reason):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 32 | 32 | M3-010 (implicit reference and boxing), M4-005 (downcasts), M4-002 (floating-point and decimal) |
| PropertyReference | 32 | 32 | M3-010 |
| DelegateCreation | 26 | 26 | M4-004 |
| no-body | 22 | 22 | M4-008 |
| ConstructorBodyOperation | 20 | 20 | M4-001 |
| Await | 8 | 8 | M3-026, M4-006 |
| missing-return | 7 | 7 | M3-026 |
| foreach-enumerator | 6 | 6 | M4-001 |
| Binary | 4 | 4 | M4-002 |
| ArrayCreation | 2 | 2 | none → P2-001 |
| ref-argument | 2 | 2 | M4-003 |
| TypeOf | 2 | 2 | none → P2-002 |
| Block | 1 | 1 | M4-008 |
| DefaultValue | 1 | 1 | none → P2-003 |
| EventReference | 1 | 1 | none → P2-004 |

Below the top fifteen: IsNull 1/1 (P2-008), lock 1/1 (M4-003), undefined 1/1 (P2-009).

## Tests (agent's report)
- legacy: not run (legacy test projects reference an unrestored `packages/` folder); modern 21 pass, 0 fail, 0 skip
- Passed on legacy, failed on modern: none known

## Findings
- Before the run environment set `NuGetAudit=false`, every modern project was skipped because NuGet
  audit warning NU1903 is reported as a load failure: P2-012.
- The legacy SDK-style `net452` projects need the .NET SDK resolver, which VS 2026 Build Tools on
  this box lacks, and reference assemblies older than 4.7.2. The run set `MSBuildSDKsPath`,
  `MSBuildEnableWorkloadResolver=false` and `TargetFrameworkRootPath`: P2-014.
- One procedure is listed in `run.properties.unverified` and one tool notification was written;
  both are recorded here as counts only.
