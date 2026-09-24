# census run: pmb-lethek__signalr.extras.autofac

- Pair: agent, lethek/SignalR.Extras.Autofac, legacy 3a4ac841ad23, modern agent migration (reused
  from M3-022, no new migration per M3-031 criterion 1)
- Corpus list: Poly-MigrationBench @ b0a91412d64a14b7fd61319591bd4b00a574f4c2
- Migrated by: Claude Code subagent, Sonnet 5, 2026-09-23 (M3-022)
- equiv: 51fd745, mode census, wall-clock 7s, exit 0

## Load
- Projects: legacy 2/2 C# in scope (5 `.csproj` in the solution, 3 not built: ExampleUsingOWIN,
  ExampleUsingIIS, `_build`), modern 2/2 C# in scope (same 3 not built); skipped: none
- Project load rate: 100% legacy, 100% modern. M3-022 reported 40% for this pair before P2-013
  changed the denominator to exclude projects the solution's default configuration does not build.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 25 | 26 |
| analysed lines | n/a (not printed by `-Metrics`; see console.txt) |  |

- Matched pairs 25; without opaque 6 (24.0%); whole-body opaque 11 (44.0%); congruent 0 (n/a)
- Unchanged share: 100% (`-Unchanged` proxy)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 100%. Not the row above; ADR 0034.

Top opaque reasons (up to 15, by legacy count):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| using | 6 | 6 | M4-001 |
| IsNull | 4 | 4 | P2-008 (backlog) |
| ConstructorBodyOperation | 3 | 3 | M4-001 |
| EventReference | 3 | 3 | P2-004 (backlog) |
| Conversion | 2 | 2 | none |
| foreach-enumerator | 2 | 2 | M4-001 |
| ArrayCreation | 1 | 1 | P2-001 |
| DelegateCreation | 1 | 1 | M4-004 |
| EventAssignment | 1 | 1 | P2-005 (backlog) |
| ref-argument | 1 | 1 | M4-003 (backlog) |
| switch-pattern | 1 | 1 | M4-005 (backlog) |
| TypeOf | 1 | 1 | P2-002 (backlog) |

## Changed code
- Changed pairs 0 of 25; without opaque 0; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): n/a: no changed pairs

Pure retarget, as M3-022 found; excluded from the lowerable-share median (ADR 0034).

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: n/a — `corpus.ps1 -Packages` fails (P2-015).

## Verdicts (full and seeded only)
n/a (`census` mode).

## Tests (full only)
n/a (`census` mode).

## Seeds (seeded only)
n/a (`census` mode).

## Findings
- Project load rate rose from 40% (M3-022) to 100%: P2-013 fixed the denominator, not the load
  path; no new finding.
