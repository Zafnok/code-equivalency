# census run: pmb-shiningrush__serviceant

- Pair: agent, ShiningRush/ServiceAnt, legacy e36009c2ee86, modern agent migration (reused from
  M3-022, no new migration per M3-031 criterion 1)
- Corpus list: Poly-MigrationBench @ b0a91412d64a14b7fd61319591bd4b00a574f4c2
- Migrated by: Claude Code subagent, Sonnet 5, 2026-09-23 (M3-022)
- equiv: 51fd745, mode census, wall-clock 11s, exit 0

## Load
- Projects: legacy 6/6 C#, modern 6/6 C#; skipped: none
- Project load rate: 100% legacy, 100% modern

## Census
| | legacy | modern |
|---|---|---|
| procedures | 136 | 137 |
| analysed lines | n/a (not printed by `-Metrics`; see console.txt) |  |

- Matched pairs 136; without opaque 39 (28.7%); whole-body opaque 58 (42.6%); congruent 0 (n/a)
- Unchanged share: 100% (`-Unchanged` proxy)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 100%. Not the row above; ADR 0034.

Top opaque reasons (up to 15, by legacy count):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| no-body | 22 | 22 | M4-008 (backlog) |
| ConstructorBodyOperation | 20 | 20 | M4-001 |
| DelegateCreation | 25 | 25 | M4-004 |
| async | 10 | 10 | M4-006 (backlog) |
| TypeOf | 9 | 9 | P2-002 (backlog) |
| Conversion | 5 | 5 | none |
| Binary | 4 | 4 | M4-002 |
| foreach-enumerator | 4 | 4 | M4-001 |
| ref-argument | 2 | 2 | M4-003 (backlog) |
| Block | 1 | 1 | M4-008 (backlog) |
| DefaultValue | 1 | 1 | P2-003 (backlog) |
| EventReference | 1 | 1 | P2-004 (backlog) |
| IsNull | 1 | 1 | P2-008 (backlog) |
| lock | 1 | 1 | M4-003 (backlog) |

## Changed code
- Changed pairs 0 of 136; without opaque 0; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): n/a: no changed pairs

The migration prompt forbids modernising code that already compiles; every `.cs` file is
byte-identical (ADR 0034's context), so this pure retarget contributes nothing to the changed-pair
metrics and is excluded from the lowerable-share median (ADR 0034 evaluation rule).

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
- None new for this pair; consistent with M3-022 (100% load rate, pure retarget).
