# census run: pmb-tomasjohansson__adapters-shortest-paths-dotnet

- Pair: agent, TomasJohansson/adapters-shortest-paths-dotnet, legacy e3722e971d86, modern agent migration (local commit 042f2f99207c)
- Corpus list: Poly-MigrationBench @ b0a91412d64a
- Migrated by: Claude Code subagent / Sonnet 5 / 2026-09-23, prompt `tools/corpus/migration-prompt.md`
- equiv: 87b730e, mode census, wall-clock 9 s, exit 0

## Load
- Projects: legacy 21/21 (13 projects, multi-targeted flavours counted separately), modern 13/13; skipped: none
- Project load rate: 100%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 317 | 313 |
| analysed lines | 7,586 | 7,439 |

- Matched pairs 312; without opaque 80 (25.6%); whole-body opaque 73 (23.4%); congruent n/a (M3-015 not landed)
- Unchanged share: 100% ("unchanged files" proxy: 153 of 153 legacy `.cs` files, 13,237 of 13,237 lines)
- Lowerable share: 25.6%

Top opaque reasons (up to 15; bodies holding at least one opaque with the reason):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 112 | 112 | M3-010 (implicit reference and boxing), M4-005 (downcasts), M4-002 (floating-point and decimal) |
| PropertyReference | 57 | 57 | M3-010 |
| Binary | 25 | 25 | M4-002 |
| ConstructorBodyOperation | 23 | 23 | M4-001 |
| DelegateCreation | 23 | 23 | M4-004 |
| foreach-enumerator | 18 | 18 | M4-001 |
| no-body | 16 | 16 | M4-008 |
| Block | 13 | 13 | M4-008 |
| ArrayCreation | 7 | 7 | none → P2-001 |
| FlowCaptureReference | 4 | 4 | none → P2-006 |
| TypeOf | 3 | 3 | none → P2-002 |
| using | 3 | 3 | M4-001 |
| CompoundAssignment | 2 | 2 | M3-010 (property target), M4-002 (non-integral) |
| FieldReference | 2 | 2 | none → P2-007 |
| Decrement | 1 | 1 | M3-010 (property target), M4-002 (non-integral) |

Below the top fifteen: IsType 1/1 (M4-005).

## Tests (agent's report)
- legacy: not run; modern 98 pass, 0 fail, 0 skip
- Passed on legacy, failed on modern: none known
- The agent raised NHibernate 5.2.7 to 5.5.2 (proxy validation fails on .NET 10) and added its own
  `global.json` without a test runner, because this repository's `global.json` forced
  Microsoft.Testing.Platform onto the corpus before `.corpus/` was isolated.

## Findings
- Before the run environment set `NoWarn=NU1701;NU1702;NU1903` for restore, the legacy
  `Adapter.QuikGraph (netstandard2.0)` flavour was skipped because a NuGet compatibility warning
  (the ProjectReference "resolved using .NETFramework" message) is reported as a load failure: P2-012.
- `net20`/`net35` flavours need the matching reference-assembly packages: P2-014.
- One procedure is listed in `run.properties.unverified`; one tool notification; results EQ004 1,
  EQ005 5 (added and removed procedures).
